import { ArrowBendDownRight, ArrowCircleUp, ChatCircleDots, CheckCircle, CircleNotch, ClockCounterClockwise, File as FileIcon, Lightbulb, Paperclip, Plus, ShareNetwork, StopCircle, Trash, X } from "@phosphor-icons/react";
import { FormEvent, ReactElement, useEffect, useRef, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { Button, Empty, ErrorState, Loading, Modal, Note } from "../components/ui";
import { ApiError, api, useAsync, useAuth, useLang, useToast } from "../core";
import { A2Action, A2Surface, A2Surfaces, applyMessages, writeData } from "./a2ui";
import { AgEvent, AgMessage, newId, parseToolResult, runAgent } from "./agui";

type Tool = { id: string; name: string; done: boolean };
/** One chat turn. Hidden user turns carry A2UI action events (Submit, Approve, Cancel) to the agent. */
type Turn = { id: string; role: "user" | "assistant"; text: string; hidden?: boolean; tools?: Tool[]; surfaces?: string[]; next?: string[] };
type Saved = { thread: string; turns: Turn[]; surfaces: A2Surfaces };
type Info = { is_enabled: boolean; examples_ar: string; examples_en: string; history_turns: number };
type Suggestion = { key: string; count: number; text_ar: string; text_en: string; ask_ar: string; ask_en: string };
type ConvRow = { thread: string; title: string; shared: boolean; updated_at: string };

const KEEP = 60;

/** Records the backoffice opens by id, so the agent can resolve "this applicant" and similar. */
const RECORDS: Record<string, [string, string]> = {
  jobs: ["Job", "job_id"], applicants: ["Applicant", "applicant_id"], interviews: ["Interview", "interview_id"], tests: ["Test", "test_id"],
  questions: ["Question", "question_id"], marking: ["Test Marking", "attempt_id"], components: ["Assistant Component", "component_id"],
};

/** The screen the staff member has open: page, record id, the page heading and any list filters. */
export function useScreen() {
  const loc = useLocation();
  return () => {
    const parts = loc.pathname.split("/").filter(Boolean).slice(1).filter((p) => p !== "setup");
    const screen: Record<string, string | number> = { path: loc.pathname.replace(/^\/(ar|en)/, "") || "/" };
    const [section, id] = parts;
    if (section && RECORDS[section] && id && /^\d+$/.test(id)) {
      screen.page = RECORDS[section][0];
      screen[RECORDS[section][1]] = Number(id);
    } else screen.page = section ? `${section} list` : "Dashboard";
    const heading = document.querySelector("main h1")?.textContent?.trim();
    if (heading) screen.heading = heading.slice(0, 200);
    const filters = [...new URLSearchParams(loc.search)].filter(([k]) => !["page", "size"].includes(k)).map(([k, v]) => `${k}=${v}`).join(", ");
    if (filters) screen.filters = filters.slice(0, 300);
    return screen;
  };
}

function load(key: string): Saved | null {
  try {
    const raw = localStorage.getItem(key);
    return raw ? (JSON.parse(raw) as Saved) : null;
  } catch {
    return null;
  }
}

function save(key: string, s: Saved) {
  try {
    localStorage.setItem(key, JSON.stringify({ ...s, turns: s.turns.slice(-KEEP) }));
  } catch {
    /* storage is a convenience only */
  }
}

/** Renders the assistant's short Markdown safely: bold, lists and simple tables. */
function Markdown({ text }: { text: string }) {
  const lines = text.split("\n");
  const out: ReactElement[] = [];
  let i = 0;
  const inline = (s: string) => s.split(/(\*\*[^*]+\*\*)/g).map((p, k) => (p.startsWith("**") && p.endsWith("**") ? <strong key={k}>{p.slice(2, -2)}</strong> : <span key={k}>{p}</span>));
  while (i < lines.length) {
    const l = lines[i];
    if (l.trim().startsWith("|")) {
      const rows: string[][] = [];
      while (i < lines.length && lines[i].trim().startsWith("|")) {
        const cells = lines[i].trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim());
        if (!cells.every((c) => /^:?-+:?$/.test(c))) rows.push(cells);
        i++;
      }
      out.push(
        <div key={`t${i}`} className="my-2 overflow-x-auto">
          <table className="mh-table"><thead><tr>{rows[0]?.map((c, k) => <th key={k}>{inline(c)}</th>)}</tr></thead>
            <tbody>{rows.slice(1).map((r, k) => <tr key={k}>{r.map((c, j) => <td key={j}>{inline(c)}</td>)}</tr>)}</tbody></table>
        </div>,
      );
      continue;
    }
    if (/^\s*([-*]|\d+\.)\s+/.test(l)) {
      const items: string[] = [];
      while (i < lines.length && /^\s*([-*]|\d+\.)\s+/.test(lines[i])) items.push(lines[i++].replace(/^\s*([-*]|\d+\.)\s+/, ""));
      out.push(<ul key={`l${i}`} className="my-1 list-disc ps-5">{items.map((it, k) => <li key={k}>{inline(it)}</li>)}</ul>);
      continue;
    }
    if (l.trim()) out.push(<p key={`p${i}`} className="my-1">{inline(l.replace(/^#+\s*/, ""))}</p>);
    i++;
  }
  return <div dir="auto" className="[&>*:first-child]:mt-0 [&>*:last-child]:mb-0">{out}</div>;
}

/** The assistant chat: AG-UI run streaming, A2UI surfaces, and the open screen as context. Used by the page and the side panel. */
export function AgentChat({ panel = false }: { panel?: boolean }) {
  const { t, lang, errorText } = useLang();
  const nav = useNavigate();
  const { me } = useAuth();
  const key = `mh-agent-${me?.id ?? 0}`;
  const toast = useToast();
  const [state, setState] = useState<Saved>(() => load(key) ?? { thread: newId(), turns: [], surfaces: {} });
  const info = useAsync(() => api<Info>("/agent/info"), []);
  const screen = useScreen();
  const [text, setText] = useState("");
  const [files, setFiles] = useState<{ id: string; name: string }[]>([]);
  const [uploading, setUploading] = useState(false);
  const picker = useRef<HTMLInputElement>(null);
  const attach = async (list: FileList | null) => {
    if (!list?.length) return;
    setUploading(true);
    try {
      for (const f of [...list].slice(0, 5)) {
        const form = new FormData();
        form.append("file", f);
        const r = await api<{ id: string; file_name: string }>("/agent/attachments", { body: form });
        setFiles((x) => [...x, { id: r.id, name: r.file_name }]);
      }
    } catch (e) {
      toast(errorText(e instanceof ApiError ? e : new ApiError("server", 500)), "error");
    } finally {
      setUploading(false);
      if (picker.current) picker.current.value = "";
    }
  };
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [acting, setActing] = useState<string | null>(null);
  const stop = useRef<AbortController | null>(null);
  const end = useRef<HTMLDivElement>(null);

  useEffect(() => save(key, state), [key, state]);
  const [params, setParams] = useSearchParams();
  const [sharedBy, setSharedBy] = useState("");
  const [history, setHistory] = useState(false);
  const suggestions = useAsync(() => api<Suggestion[]>("/agent/suggestions").catch(() => [] as Suggestion[]), []);
  // The conversation is also kept on the server (after each reply), so it follows the person to other devices and can be shared.
  const title = state.turns.find((x) => x.role === "user" && !x.hidden)?.text.slice(0, 120) || "";
  useEffect(() => {
    if (running || !title || sharedBy) return;
    const timer = setTimeout(() => {
      api(`/agent/conversations/${encodeURIComponent(state.thread)}`, { method: "PUT", body: { title, content: { ...state, turns: state.turns.slice(-KEEP) } } }).catch(() => { /* kept locally anyway */ });
    }, 1500);
    return () => clearTimeout(timer);
  }, [state, running, title, sharedBy]);
  const openConversation = async (thread: string) => {
    try {
      const c = await api<{ mine: boolean; owner: string; content: Saved }>(`/agent/conversations/${encodeURIComponent(thread)}`);
      const content = { turns: c.content.turns || [], surfaces: c.content.surfaces || {} };
      // A colleague's shared conversation opens as the reader's own copy; theirs is never changed.
      setState(c.mine ? { ...content, thread } : { ...content, thread: newId() });
      setSharedBy(c.mine ? "" : c.owner);
      setHistory(false);
    } catch (e) {
      toast(errorText(e instanceof ApiError ? e : new ApiError("server", 500)), "error");
    }
  };
  useEffect(() => {
    // Only the assistant page takes ?c= (a conversation) and ?q= (a question to ask); lists use ?q= for their search.
    if (panel) return;
    const c = params.get("c");
    const q = params.get("q");
    if (!c && !q) return;
    const next = new URLSearchParams(params);
    next.delete("c");
    next.delete("q");
    setParams(next, { replace: true });
    if (c) openConversation(c);
    else if (q) ask(q);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const share = async () => {
    try {
      await api(`/agent/conversations/${encodeURIComponent(state.thread)}`, { method: "PUT", body: { title, content: { ...state, turns: state.turns.slice(-KEEP) } } });
      await api(`/agent/conversations/${encodeURIComponent(state.thread)}/share`, { body: { shared: true } });
      const link = `${location.origin}/${lang}/assistant?c=${encodeURIComponent(state.thread)}`;
      try { await navigator.clipboard.writeText(link); toast(t("asst.share_copied")); } catch { toast(link, "info"); }
    } catch (e) {
      toast(errorText(e instanceof ApiError ? e : new ApiError("server", 500)), "error");
    }
  };
  // Scroll only the message list (scrollIntoView can move the whole page on phones).
  const list = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = list.current;
    if (el) el.scrollTo({ top: el.scrollHeight, behavior: "smooth" });
  }, [state.turns, running]);
  useEffect(() => () => stop.current?.abort(), []);

  const ask = async (q: string, hidden = false) => {
    setError(null);
    const user: Turn = { id: newId(), role: "user", text: q, hidden };
    const reply: Turn = { id: newId(), role: "assistant", text: "", tools: [], surfaces: [] };
    const history: AgMessage[] = [...state.turns, user]
      .filter((x) => x.text.trim())
      .slice(-(info.data?.history_turns ?? 30))
      .map((x) => ({ id: x.id, role: x.role, content: x.text }));
    setState((s) => ({ ...s, turns: [...s.turns, user, reply] }));
    const patch = (f: (r: Turn, s: Saved) => Partial<Turn> & { surfaces?: string[] }, surfaces?: (s: A2Surfaces) => A2Surfaces) =>
      setState((s) => ({
        ...s,
        surfaces: surfaces ? surfaces(s.surfaces) : s.surfaces,
        turns: s.turns.map((x) => (x.id === reply.id ? { ...x, ...f(x, s) } : x)),
      }));
    let lastMessage = "";
    const onEvent = (e: AgEvent) => {
      switch (e.type) {
        case "TEXT_MESSAGE_CONTENT": {
          const ev = e as { messageId: string; delta: string };
          const gap = lastMessage && lastMessage !== ev.messageId ? "\n\n" : "";
          lastMessage = ev.messageId;
          patch((r) => ({ text: r.text + gap + ev.delta }));
          break;
        }
        case "TOOL_CALL_START": {
          const ev = e as { toolCallId: string; toolCallName: string };
          patch((r) => ({ tools: [...(r.tools ?? []), { id: ev.toolCallId, name: ev.toolCallName, done: false }] }));
          break;
        }
        case "TOOL_CALL_RESULT": {
          const ev = e as { toolCallId: string; content: string };
          const result = parseToolResult(ev.content);
          const a2ui = result && Array.isArray(result.a2ui) ? result.a2ui : null;
          // Suggested next steps: shown as buttons under the reply (not as a tool step).
          if (result && Array.isArray(result.next_actions)) {
            const next = (result.next_actions as unknown[]).filter((x): x is string => typeof x === "string" && x.trim().length > 0).slice(0, 4);
            patch((r) => ({ next, tools: r.tools?.filter((x) => x.id !== ev.toolCallId) }));
            break;
          }
          let touched: string[] = [];
          patch(
            (r) => ({ tools: r.tools?.map((x) => (x.id === ev.toolCallId ? { ...x, done: true } : x)), surfaces: [...new Set([...(r.surfaces ?? []), ...touched])] }),
            a2ui ? (all) => { const out = applyMessages(all, a2ui); touched = out.touched; return out.surfaces; } : undefined,
          );
          break;
        }
        case "RUN_ERROR":
          setError(new ApiError(String((e as { code?: string }).code || "server"), 500));
          break;
      }
    };
    setRunning(true);
    stop.current = new AbortController();
    try {
      await runAgent(state.thread, history, lang, onEvent, stop.current.signal, screen());
    } catch (e) {
      setError(e instanceof ApiError ? e : new ApiError("server", 500));
    } finally {
      setRunning(false);
      stop.current = null;
      setState((s) => ({ ...s, turns: s.turns.map((x) => (x.id === reply.id ? { ...x, tools: x.tools?.map((y) => ({ ...y, done: true })) } : x)) }));
    }
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const typed = text.trim();
    if ((!typed && files.length === 0) || running || uploading) return;
    // Attachments travel with the message as references the assistant reads with read_attachment.
    const q = [typed || (lang === "en" ? "Please look at the attached file." : "يرجى الاطلاع على الملف المرفق."), ...files.map((f) => `[Attachment: ${f.name}, id: ${f.id}]`)].join("\n");
    setText("");
    setFiles([]);
    ask(q);
  };

  /** Button events from A2UI surfaces. Approvals go to the backend, which carries the change out; the agent is told afterwards. */
  const onAction = async (a: A2Action) => {
    if (a.name === "open_applicant") return nav(`/${lang}/applicants/${a.context.applicant_id}`);
    if (a.name === "submit_form") {
      // The answers go back to the agent as an A2UI action, and it carries on.
      if (running) return;
      setState((s) => ({
        ...s,
        surfaces: applyMessages(s.surfaces, [
          { version: "1.0", updateDataModel: { surfaceId: a.surfaceId, path: "/state", value: "submitted" } },
          { version: "1.0", updateComponents: { surfaceId: a.surfaceId, components: [{ id: "actions", component: "Text", text: t("asst.sent"), variant: "h5" }] } },
        ]).surfaces,
      }));
      return ask(JSON.stringify({ version: "1.0", action: a }), true);
    }
    if (a.name !== "approve_proposal" && a.name !== "cancel_proposal") return;
    const approve = a.name === "approve_proposal";
    setActing(a.surfaceId);
    try {
      const r = await api<{ status: string; result: string; link?: string | null }>(
        `/agent/proposals/${encodeURIComponent(a.context.proposal_id)}/${approve ? "approve" : "cancel"}?lang=${lang}`,
        { body: approve && a.context.changes && typeof a.context.changes === "object" ? { changes: a.context.changes } : {} });
      const done = approve ? r.result : t("asst.cancelled");
      const event: Turn = { id: newId(), role: "user", hidden: true, text: JSON.stringify({ version: "1.0", action: { ...a, context: { ...a.context, status: r.status, result: r.result } } }) };
      setState((s) => ({
        ...s,
        turns: [...s.turns, event],
        surfaces: applyMessages(s.surfaces, [
          { version: "1.0", updateDataModel: { surfaceId: a.surfaceId, path: "/state", value: approve ? "approved" : "cancelled" } },
          { version: "1.0", updateComponents: { surfaceId: a.surfaceId, components: [{ id: "actions", component: "Text", text: done, variant: "h5" }] } },
        ]).surfaces,
      }));
      if (approve) toast(r.result);
    } catch (e) {
      toast(errorText(e instanceof ApiError ? e : new ApiError("server", 500)), "error");
    } finally {
      setActing(null);
    }
  };

  const examples = ((lang === "en" ? info.data?.examples_en : info.data?.examples_ar) || t("asst.examples_default")).split("\n").map((x) => x.trim()).filter(Boolean).slice(0, 6);
  if (info.data && !info.data.is_enabled) return <Note tone="warning">{t("error.assistant_off")}</Note>;
  // An assistant turn shows once something arrives (a step, text or a card); until then the typing dots stand in for it.
  const visible = state.turns.filter((x) => !x.hidden && (x.role === "user" || x.text.trim() || x.tools?.length || x.surfaces?.length));
  return (
    <>
      <div className={`flex flex-col bg-whitely-50 ${panel ? "h-full" : "mc-surface h-[calc(100dvh-11rem)] min-h-[26rem] rounded-2xl border border-aeblack-100 sm:h-[calc(100vh-16rem)]"}`}>
        {/* The open screen still goes to the assistant with each message; it is not shown here. */}
        <div className="flex items-center justify-end gap-2 border-b border-aeblack-100 bg-white px-4 py-2">
          <Button variant="link" size="sm" onClick={() => setHistory(true)} icon={<ClockCounterClockwise size={20} aria-hidden="true" />}>
            <span className={panel ? "sr-only" : "hidden sm:inline"}>{t("asst.history")}</span></Button>
          {title && !running && <Button variant="link" size="sm" onClick={share} icon={<ShareNetwork size={20} aria-hidden="true" />}>
            <span className={panel ? "sr-only" : "hidden sm:inline"}>{t("asst.share")}</span></Button>}
          <Button variant="link" size="sm" disabled={running} onClick={() => { setState({ thread: newId(), turns: [], surfaces: {} }); setSharedBy(""); }}
            icon={<Plus size={20} aria-hidden="true" />}>{t("asst.new_chat")}</Button>
        </div>
        <div ref={list} className="min-h-0 flex-1 space-y-4 overflow-y-auto overscroll-contain p-4 sm:p-6" aria-live="polite">
          {sharedBy && <Note>{t("asst.shared_by", { name: sharedBy })}</Note>}
          {visible.length === 0 && (
            <div className="space-y-3">
              <Note>{t("asst.intro")}</Note>
              {(suggestions.data?.length ?? 0) > 0 && (
                <div>
                  <p className="mb-2 flex items-center gap-1.5 text-sm font-semibold text-aeblack-700"><Lightbulb size={20} className="text-primary-600" aria-hidden="true" />{t("asst.suggested")}</p>
                  <ul className="space-y-2">{suggestions.data!.map((x) => (
                    <li key={x.key}><button type="button" className="w-full rounded-xl border border-aeblack-100 bg-white px-3 py-2 text-start text-sm hover:border-primary-300 hover:bg-primary-50"
                      onClick={() => ask(lang === "en" ? x.ask_en : x.ask_ar)}>{lang === "en" ? x.text_en : x.text_ar}</button></li>
                  ))}</ul>
                </div>
              )}
              {examples.length > 0 && <ul className="flex flex-wrap gap-2">{examples.map((x) => (
                <li key={x}><button type="button" className="rounded-full border border-primary-200 bg-white px-3 py-1.5 text-start text-sm text-primary-800 hover:bg-primary-50"
                  onClick={() => ask(x)}>{x}</button></li>
              ))}</ul>}
            </div>
          )}
          {visible.map((m) => (
            <div key={m.id} className={`flex ${m.role === "user" ? "justify-end" : "justify-start"}`}>
              <div className={`max-w-[92%] rounded-2xl text-sm leading-6 ${panel ? "" : "sm:max-w-[85%]"} ${m.role === "user" ? "mh-chat-user px-4 py-3"
                : m.surfaces?.some((id) => state.surfaces[id]) ? "w-full py-1" /* cards carry their own frame: no second border around them */ : "mh-chat-bot w-full px-4 py-3"}`}>
                {m.role === "assistant" && <ChatCircleDots size={20} className="mb-1 text-primary-600" aria-hidden="true" />}
                {m.tools && m.tools.length > 0 && (
                  <ul className={`flex flex-wrap gap-2 ${m.text.trim() || m.surfaces?.length ? "mb-2" : "mb-0"}`} aria-label={t("asst.thinking")}>
                    {m.tools.filter((x) => x.name !== "suggest_next_actions").map((x) => (
                      <li key={x.id} className="inline-flex items-center gap-1 rounded-full bg-whitely-100 px-3 py-1 text-xs text-aeblack-600">
                        {x.done ? <CheckCircle size={16} className="text-aegreen-600" aria-hidden="true" /> : <CircleNotch size={16} className="animate-spin" aria-hidden="true" />}
                        {t(`asst.tool.${x.name}`) === `asst.tool.${x.name}` ? x.name : t(`asst.tool.${x.name}`)}
                      </li>
                    ))}
                  </ul>
                )}
                {/* m-0: the design system gives paragraphs a bottom margin, which left an empty line inside the bubble. */}
                {m.role === "user" ? <UserText text={m.text} /> : m.text.trim() && <Markdown text={m.text} />}
                {m.surfaces?.map((id) => state.surfaces[id] && <A2Surface key={id} surface={state.surfaces[id]} busy={acting === id} onAction={onAction} emptyText={t("common.choose")}
                  onData={(sid, path, value) => setState((s) => ({ ...s, surfaces: writeData(s.surfaces, sid, path, value) }))} />)}
                {m.role === "assistant" && m.next && m.next.length > 0 && m.id === visible[visible.length - 1]?.id && !running && (
                  <div className="mt-3 border-t border-aeblack-100 pt-3">
                    <p className="mb-2 text-xs font-semibold text-aeblack-500">{t("asst.next")}</p>
                    <ul className="flex flex-wrap gap-2">
                      {m.next.map((q) => (
                        <li key={q}><button type="button" onClick={() => ask(q)} dir="auto"
                          className="inline-flex items-center gap-1 rounded-full border border-primary-200 bg-primary-50 px-3 py-1.5 text-start text-xs font-semibold text-primary-800 hover:bg-primary-100 focus-visible:outline-2">
                          <ArrowBendDownRight size={14} className="shrink-0 rtl:-scale-x-100" aria-hidden="true" />{q}
                        </button></li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            </div>
          ))}
          {running && (
            <div className="flex justify-start" role="status">
              <span className="mh-typing" aria-hidden="true"><i /><i /><i /></span>
              <span className="sr-only">{t("asst.thinking")}</span>
            </div>
          )}
          {error && <ErrorState error={error} />}
          <div ref={end} />
        </div>
        {files.length > 0 && (
          <ul className="flex flex-wrap gap-2 border-t border-aeblack-100 bg-white px-3 pt-2" aria-label={t("asst.attached")}>
            {files.map((f) => (
              <li key={f.id} className="inline-flex items-center gap-1 rounded-full bg-primary-50 px-3 py-1 text-xs text-primary-800">
                <FileIcon size={16} aria-hidden="true" /><span className="max-w-48 truncate" dir="auto">{f.name}</span>
                <button type="button" className="rounded-full p-0.5 hover:bg-primary-100" aria-label={t("asst.remove_attachment")} onClick={() => setFiles((x) => x.filter((y) => y.id !== f.id))}><X size={14} /></button>
              </li>
            ))}
          </ul>
        )}
        <form onSubmit={submit} className="flex items-end gap-2 border-t border-aeblack-100 bg-white p-3">
          <input ref={picker} type="file" className="hidden" multiple accept=".pdf,.docx,.txt,.csv,.md,.png,.jpg,.jpeg" onChange={(e) => attach(e.target.files)} />
          <Button variant="outline" aria-label={t("asst.attach")} title={t("asst.attach_hint")} busy={uploading} disabled={running}
            onClick={() => picker.current?.click()} icon={<Paperclip size={24} aria-hidden="true" />} />
          <label htmlFor="asst-q" className="sr-only">{t("asst.placeholder")}</label>
          <textarea id="asst-q" rows={2} dir="auto" className="flex-1 resize-none rounded-xl border border-aeblack-200 p-3 outline-none focus:border-primary-500"
            placeholder={t("asst.placeholder")} value={text} onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); (e.currentTarget.form as HTMLFormElement).requestSubmit(); } }} />
          {running
            ? <Button variant="outline" aria-label={t("asst.stop")} onClick={() => stop.current?.abort()} icon={<StopCircle size={24} aria-hidden="true" />}><span className="hidden sm:inline">{t("asst.stop")}</span></Button>
            : <Button type="submit" aria-label={t("asst.send")} disabled={(!text.trim() && files.length === 0) || uploading} icon={<ArrowCircleUp size={24} aria-hidden="true" />}><span className="hidden sm:inline">{t("asst.send")}</span></Button>}
        </form>
      </div>
      {history && <History onClose={() => setHistory(false)} onOpen={openConversation} current={state.thread} />}
    </>
  );
}

/** The staff member's saved conversations: open one again or delete it. */
function History({ onClose, onOpen, current }: { onClose: () => void; onOpen: (thread: string) => void; current: string }) {
  const { t, date } = useLang();
  const list = useAsync(() => api<{ items: ConvRow[] }>("/agent/conversations?size=50"), []);
  const remove = async (thread: string) => {
    await api(`/agent/conversations/${encodeURIComponent(thread)}`, { method: "DELETE" }).catch(() => undefined);
    list.reload();
  };
  return (
    <Modal open onClose={onClose} title={t("asst.history")}>
      {list.loading && !list.data ? <Loading /> : list.error ? <ErrorState error={list.error} /> : (list.data?.items.length ?? 0) === 0 ? <Empty /> : (
        <ul className="divide-y divide-aeblack-100">
          {list.data!.items.map((c) => (
            <li key={c.thread} className="flex items-center justify-between gap-3 py-2">
              <button type="button" className="min-w-0 flex-1 text-start" onClick={() => onOpen(c.thread)}>
                <span className={`block truncate font-semibold ${c.thread === current ? "text-primary-700" : "text-aeblack-900"}`} dir="auto">{c.title}</span>
                <span className="text-xs text-aeblack-500">{date(c.updated_at, true)}{c.shared && <> · {t("asst.shared")}</>}</span>
              </button>
              <Button variant="link" size="sm" aria-label={t("common.delete")} onClick={() => remove(c.thread)}><Trash size={20} aria-hidden="true" /></Button>
            </li>
          ))}
        </ul>
      )}
    </Modal>
  );
}

/** A user message: its text, and any attached files as chips (the references the assistant reads stay out of sight). */
function UserText({ text }: { text: string }) {
  const lines = text.split("\n");
  const files = lines.map((l) => /^\[Attachment: (.+), id: [0-9a-f-]{36}\]$/.exec(l)).filter((m): m is RegExpExecArray => !!m).map((m) => m[1]);
  const body = lines.filter((l) => !/^\[Attachment: .+, id: [0-9a-f-]{36}\]$/.test(l)).join("\n");
  return (
    <>
      <p className="m-0 whitespace-pre-line" dir="auto">{body}</p>
      {files.length > 0 && (
        <ul className="mt-2 flex flex-wrap gap-1.5">
          {files.map((f, i) => <li key={i} className="inline-flex items-center gap-1 rounded-full bg-white/70 px-2 py-0.5 text-xs"><FileIcon size={14} aria-hidden="true" /><span dir="auto">{f}</span></li>)}
        </ul>
      )}
    </>
  );
}
