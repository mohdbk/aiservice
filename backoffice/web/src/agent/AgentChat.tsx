import { ArrowBendDownRight, ArrowsIn, ArrowsOut, ArrowUp, CaretDown, CaretRight, CheckCircle, CircleNotch, ClockCounterClockwise, Eye, File as FileIcon, Lightbulb, NotePencil, Paperclip, ShareNetwork, ShieldCheck, Sparkle, Stop, Trash, X } from "@phosphor-icons/react";
import { FormEvent, ReactElement, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
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
  const inline = (s: string) => s.split(/(\*\*[^*]+\*\*|`[^`]+`)/g).map((p, k) => (p.startsWith("**") && p.endsWith("**") ? <strong key={k}>{p.slice(2, -2)}</strong>
    : p.length > 2 && p.startsWith("`") && p.endsWith("`") ? <span key={k} className="font-semibold">{p.slice(1, -1).replace(/_/g, " ")}</span> : <span key={k}>{p}</span>));
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
export function AgentChat({ panel = false, onClose }: { panel?: boolean; onClose?: () => void }) {
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
  const [expanded, setExpanded] = useState(false);
  // The message box grows with what is typed, up to about six lines.
  const box = useRef<HTMLTextAreaElement>(null);
  const grow = (el: HTMLTextAreaElement) => { el.style.height = "auto"; el.style.height = `${Math.min(el.scrollHeight, 168)}px`; };
  useEffect(() => { if (!text && box.current) box.current.style.height = ""; }, [text]);
  useEffect(() => { box.current?.focus(); }, [expanded]);
  // Full screen hides the page behind it from scrolling.
  useEffect(() => {
    if (!expanded) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.body.style.overflow = prev; };
  }, [expanded]);

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
  // An assistant turn shows once something arrives (a step, text or a card); until then the working line stands in for it.
  const visible = state.turns.filter((x) => !x.hidden && (x.role === "user" || x.text.trim() || x.tools?.length || x.surfaces?.length));
  const last = state.turns[state.turns.length - 1];
  const step = running ? [...(last?.tools ?? [])].reverse().find((x) => !x.done && x.name !== "suggest_next_actions") : undefined;
  const label = (name: string) => (t(`asst.tool.${name}`) === `asst.tool.${name}` ? name : t(`asst.tool.${name}`));
  const hour = new Date().getHours();
  const greet = t(hour < 12 ? "asst.greet_morning" : hour < 18 ? "asst.greet_afternoon" : "asst.greet_evening", { name: (me?.full_name || "").split(" ")[0] });
  const where = panel ? screen().heading : undefined;

  const body = (
    <div className={`mh-asst flex flex-col ${expanded ? "fixed inset-0 z-[60] bg-whitely-50" : panel ? "h-full" : "mc-surface h-[calc(100dvh-11rem)] min-h-[26rem] overflow-hidden rounded-2xl border border-aeblack-100 sm:h-[calc(100vh-16rem)]"}`}
      role={expanded ? "dialog" : undefined} aria-modal={expanded || undefined} aria-label={expanded ? t("asst.title") : undefined}
      onKeyDown={(e) => { if (e.key === "Escape" && expanded) { e.stopPropagation(); setExpanded(false); } }}>
      {/* Top bar: who is answering and what it is doing, and the conversation's tools. */}
      <div className="flex items-center gap-3 border-b border-aeblack-100 bg-white px-3 py-2 sm:px-4">
        <span className={`mh-asst-avatar ${running ? "is-working" : ""}`} aria-hidden="true"><Sparkle size={20} /></span>
        <div className="min-w-0 flex-1">
          <p className="m-0 truncate text-sm font-bold text-aeblack-900">{panel && !expanded ? t("nav.assistant") : t("asst.title")}</p>
          <p className="m-0 flex items-center gap-1.5 truncate text-xs text-aeblack-500" aria-live="polite">
            <span className={`inline-block h-2 w-2 shrink-0 rounded-full ${running ? "animate-pulse bg-primary-500" : "bg-aegreen-500"}`} aria-hidden="true" />
            <span className="truncate">{running ? (step ? label(step.name) : t("asst.thinking")) : t("asst.ready")}</span>
          </p>
        </div>
        <div className="flex shrink-0 items-center">
          <IconButton label={t("asst.history")} onClick={() => setHistory(true)}><ClockCounterClockwise size={20} /></IconButton>
          {title && !running && <IconButton label={t("asst.share")} onClick={share}><ShareNetwork size={20} /></IconButton>}
          <IconButton label={t("asst.new_chat")} disabled={running} onClick={() => { setState({ thread: newId(), turns: [], surfaces: {} }); setSharedBy(""); }}><NotePencil size={20} /></IconButton>
          <IconButton label={expanded ? t("asst.collapse") : t("asst.expand")} onClick={() => setExpanded((x) => !x)}>
            {expanded ? <ArrowsIn size={20} /> : <ArrowsOut size={20} />}
          </IconButton>
          {onClose && !expanded && <IconButton label={t("app.close")} onClick={onClose}><X size={20} /></IconButton>}
        </div>
      </div>

      <div ref={list} className="min-h-0 flex-1 overflow-y-auto overscroll-contain" aria-live="polite">
        <div className={`mx-auto w-full space-y-5 p-4 sm:p-6 ${expanded ? "max-w-4xl" : ""}`}>
          {sharedBy && <Note>{t("asst.shared_by", { name: sharedBy })}</Note>}
          {visible.length === 0 && (
            <div className="space-y-5">
              <div className="mh-asst-hero rounded-2xl p-5 sm:p-6">
                <span className="mh-asst-avatar is-large mb-3" aria-hidden="true"><Sparkle size={28} /></span>
                <p className="m-0 text-xl font-bold text-aeblack-900 sm:text-2xl">{greet}</p>
                <p className="m-0 mt-1 max-w-xl text-sm leading-6 text-aeblack-700">{t("asst.intro")}</p>
              </div>
              {(suggestions.data?.length ?? 0) > 0 && (
                <section aria-labelledby="asst-suggested">
                  <h2 id="asst-suggested" className="mb-2 flex items-center gap-1.5 text-sm font-semibold text-aeblack-700"><Lightbulb size={20} className="text-primary-600" aria-hidden="true" />{t("asst.suggested")}</h2>
                  <ul className={`grid gap-2 ${panel && !expanded ? "" : "sm:grid-cols-2"}`}>{suggestions.data!.map((x) => (
                    <li key={x.key}>
                      <button type="button" className="mh-asst-card group flex h-full w-full items-center gap-3 text-start" onClick={() => ask(lang === "en" ? x.ask_en : x.ask_ar)}>
                        <span className="flex h-9 min-w-9 shrink-0 items-center justify-center rounded-lg bg-primary-50 px-2 text-sm font-bold tabular-nums text-primary-800">{x.count}</span>
                        <span className="min-w-0 flex-1 text-sm text-aeblack-800">{lang === "en" ? x.text_en : x.text_ar}</span>
                        <CaretRight size={16} className="shrink-0 text-aeblack-400 transition group-hover:translate-x-0.5 group-hover:text-primary-700 rtl:-scale-x-100 rtl:group-hover:-translate-x-0.5" aria-hidden="true" />
                      </button>
                    </li>
                  ))}</ul>
                </section>
              )}
              {examples.length > 0 && (
                <section aria-labelledby="asst-try">
                  <h2 id="asst-try" className="mb-2 text-sm font-semibold text-aeblack-700">{t("asst.try")}</h2>
                  <Chips items={examples} onPick={(x) => ask(x)} />
                </section>
              )}
            </div>
          )}
          {visible.map((m) => m.role === "user" ? (
            <div key={m.id} className="flex justify-end">
              <div className={`mh-chat-user max-w-[88%] rounded-2xl rounded-ee-md px-4 py-3 text-sm leading-6 ${panel && !expanded ? "" : "sm:max-w-[75%]"}`}><UserText text={m.text} /></div>
            </div>
          ) : (
            <div key={m.id} className="flex items-start gap-3">
              <span className="mh-asst-avatar is-small mt-0.5" aria-hidden="true"><Sparkle size={16} /></span>
              <div className="min-w-0 flex-1 space-y-3 text-sm leading-6">
                {m.tools && m.tools.some((x) => x.name !== "suggest_next_actions") && (
                  <Steps tools={m.tools.filter((x) => x.name !== "suggest_next_actions")} label={label} live={running && m.id === last?.id} />
                )}
                {m.text.trim() && <div className="mh-chat-bot rounded-2xl rounded-ss-md px-4 py-3"><Markdown text={m.text} /></div>}
                {m.surfaces?.map((id) => state.surfaces[id] && <A2Surface key={id} surface={state.surfaces[id]} busy={acting === id} onAction={onAction} emptyText={t("common.choose")}
                  onData={(sid, path, value) => setState((s) => ({ ...s, surfaces: writeData(s.surfaces, sid, path, value) }))} />)}
                {m.next && m.next.length > 0 && m.id === visible[visible.length - 1]?.id && !running && (
                  <div>
                    <p className="mb-2 text-xs font-semibold text-aeblack-500">{t("asst.next")}</p>
                    <Chips items={m.next} onPick={(q) => ask(q)} arrow />
                  </div>
                )}
              </div>
            </div>
          ))}
          {running && !(last?.text.trim() || last?.surfaces?.length || last?.tools?.length) && (
            <div className="flex items-center gap-3" role="status">
              <span className="mh-asst-avatar is-small is-working" aria-hidden="true"><Sparkle size={16} /></span>
              <span className="mh-shimmer text-sm font-medium">{t("asst.thinking")}</span>
            </div>
          )}
          {error && <ErrorState error={error} />}
          <div ref={end} />
        </div>
      </div>

      {/* Composer: attach, write and send in one frame, all on the same line. */}
      <div className="border-t border-aeblack-100 bg-white px-3 pb-2 pt-3 sm:px-4">
        <form onSubmit={submit} className={`mx-auto w-full ${expanded ? "max-w-4xl" : ""}`}>
          <div className="mh-composer">
            {files.length > 0 && (
              <ul className="flex flex-wrap gap-2 px-2 pt-2" aria-label={t("asst.attached")}>
                {files.map((f) => (
                  <li key={f.id} className="inline-flex items-center gap-1 rounded-full bg-primary-50 py-1 pe-1 ps-3 text-xs text-primary-800">
                    <FileIcon size={16} aria-hidden="true" /><span className="max-w-48 truncate" dir="auto">{f.name}</span>
                    <button type="button" className="rounded-full p-0.5 hover:bg-primary-100" aria-label={t("asst.remove_attachment")} onClick={() => setFiles((x) => x.filter((y) => y.id !== f.id))}><X size={14} /></button>
                  </li>
                ))}
              </ul>
            )}
            <div className="flex items-end gap-1 p-1.5">
              <input ref={picker} type="file" className="hidden" multiple accept=".pdf,.docx,.txt,.csv,.md,.png,.jpg,.jpeg" onChange={(e) => attach(e.target.files)} />
              <button type="button" className="mh-composer-btn" aria-label={t("asst.attach")} title={t("asst.attach_hint")} disabled={running || uploading} onClick={() => picker.current?.click()}>
                {uploading ? <CircleNotch size={22} className="animate-spin" aria-hidden="true" /> : <Paperclip size={22} aria-hidden="true" />}
              </button>
              <label htmlFor="asst-q" className="sr-only">{t("asst.placeholder")}</label>
              <textarea id="asst-q" ref={box} rows={1} dir="auto" className="mh-composer-input"
                placeholder={t("asst.placeholder")} value={text} onChange={(e) => { setText(e.target.value); grow(e.currentTarget); }}
                onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); (e.currentTarget.form as HTMLFormElement).requestSubmit(); } }} />
              {running
                ? <button type="button" className="mh-composer-btn is-stop" aria-label={t("asst.stop")} title={t("asst.stop")} onClick={() => stop.current?.abort()}><Stop size={20} aria-hidden="true" /></button>
                : <button type="submit" className="mh-composer-btn is-send" aria-label={t("asst.send")} title={t("asst.send")} disabled={(!text.trim() && files.length === 0) || uploading}>
                    <ArrowUp size={20} aria-hidden="true" /></button>}
            </div>
          </div>
          <div className="mt-1.5 flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-1 text-[11px] text-aeblack-500">
            <span className="inline-flex items-center gap-1"><ShieldCheck size={14} className="text-aegreen-600" aria-hidden="true" />{t("asst.approval_hint")}</span>
            {where ? <span className="inline-flex min-w-0 items-center gap-1"><Eye size={14} aria-hidden="true" /><span className="truncate">{t("asst.context", { page: where })}</span></span>
              : <span className="hidden sm:inline">{t("asst.enter_hint")}</span>}
          </div>
        </form>
      </div>
    </div>
  );

  return (
    <>
      {expanded ? <>{!panel && <div className="h-[calc(100vh-16rem)]" />}{createPortal(body, document.body)}</> : body}
      {history && <History onClose={() => setHistory(false)} onOpen={openConversation} current={state.thread} />}
    </>
  );
}

/** A small round toolbar button with its name as tooltip and for screen readers. */
function IconButton({ label, onClick, disabled, children }: { label: string; onClick: () => void; disabled?: boolean; children: ReactElement }) {
  return (
    <button type="button" className="flex h-9 w-9 items-center justify-center rounded-full text-aeblack-600 transition hover:bg-primary-50 hover:text-primary-800 disabled:opacity-40"
      aria-label={label} title={label} disabled={disabled} onClick={onClick}>{children}</button>
  );
}

/** Choice chips that wrap from the reading start, all the same height. Each chip keeps the page's direction; only its text follows its own language. */
function Chips({ items, onPick, arrow = false }: { items: string[]; onPick: (x: string) => void; arrow?: boolean }) {
  return (
    <ul className="flex flex-wrap justify-start gap-2">
      {items.map((x) => (
        <li key={x} className="max-w-full">
          <button type="button" onClick={() => onPick(x)}
            className="mh-asst-chip">
            {arrow && <ArrowBendDownRight size={14} className="mt-[3px] shrink-0 rtl:-scale-x-100" aria-hidden="true" />}
            <span dir="auto" className="min-w-0">{x}</span>
          </button>
        </li>
      ))}
    </ul>
  );
}

/** What the assistant looked at or prepared: live while it works, then folded into one line that opens. */
function Steps({ tools, label, live }: { tools: Tool[]; label: (name: string) => string; live: boolean }) {
  const { t } = useLang();
  const [open, setOpen] = useState(false);
  const show = live || open;
  return (
    <div className="text-xs text-aeblack-600">
      {!live && (
        <button type="button" className="inline-flex items-center gap-1.5 rounded-full px-2 py-1 font-semibold hover:bg-whitely-100" aria-expanded={open} onClick={() => setOpen((x) => !x)}>
          <CheckCircle size={16} className="text-aegreen-600" aria-hidden="true" />{t("asst.steps", { count: tools.length })}
          <CaretDown size={12} className={`transition ${open ? "rotate-180" : ""}`} aria-hidden="true" />
        </button>
      )}
      {show && (
        <ol className={`mh-asst-steps ${live ? "" : "mt-1"}`} aria-label={t("asst.thinking")}>
          {tools.map((x) => (
            <li key={x.id} className="flex items-center gap-2">
              {x.done ? <CheckCircle size={16} className="shrink-0 text-aegreen-600" aria-hidden="true" /> : <CircleNotch size={16} className="shrink-0 animate-spin text-primary-600" aria-hidden="true" />}
              <span className={x.done ? "" : "mh-shimmer font-medium"}>{label(x.name)}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
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
