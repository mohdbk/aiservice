import { ArrowCircleUp, ChatCircleDots, CheckCircle, CircleNotch, Plus, StopCircle } from "@phosphor-icons/react";
import { FormEvent, ReactElement, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { A2Action, A2Surface, A2Surfaces, applyMessages, writeData } from "../agent/a2ui";
import { AgEvent, AgMessage, newId, parseToolResult, runAgent } from "../agent/agui";
import { Button, ErrorState, Note, PageHeader } from "../components/ui";
import { ApiError, api, useAuth, useLang, usePageTitle, useToast } from "../core";

type Tool = { id: string; name: string; done: boolean };
/** One chat turn. Hidden user turns carry A2UI action events (Approve, Cancel) to the agent. */
type Turn = { id: string; role: "user" | "assistant"; text: string; hidden?: boolean; tools?: Tool[]; surfaces?: string[] };
type Saved = { thread: string; turns: Turn[]; surfaces: A2Surfaces };

const KEEP = 60;

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
  return <div dir="auto">{out}</div>;
}

export default function Assistant() {
  const { t, lang, errorText } = useLang();
  usePageTitle(t("asst.title"));
  const toast = useToast();
  const nav = useNavigate();
  const { me } = useAuth();
  const key = `mh-agent-${me?.id ?? 0}`;
  const [state, setState] = useState<Saved>(() => load(key) ?? { thread: newId(), turns: [], surfaces: {} });
  const [text, setText] = useState("");
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [acting, setActing] = useState<string | null>(null);
  const stop = useRef<AbortController | null>(null);
  const end = useRef<HTMLDivElement>(null);

  useEffect(() => save(key, state), [key, state]);
  useEffect(() => end.current?.scrollIntoView({ behavior: "smooth", block: "end" }), [state.turns.length, running]);
  useEffect(() => () => stop.current?.abort(), []);

  const ask = async (q: string, hidden = false) => {
    setError(null);
    const user: Turn = { id: newId(), role: "user", text: q, hidden };
    const reply: Turn = { id: newId(), role: "assistant", text: "", tools: [], surfaces: [] };
    const history: AgMessage[] = [...state.turns, user]
      .filter((x) => x.text.trim())
      .slice(-30)
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
      await runAgent(state.thread, history, lang, onEvent, stop.current.signal);
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
    const q = text.trim();
    if (!q || running) return;
    setText("");
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

  const visible = state.turns.filter((x) => !x.hidden);
  return (
    <>
      <PageHeader title={t("asst.title")} subtitle={t("asst.intro")}
        actions={<Button variant="outline" size="sm" disabled={running} onClick={() => setState({ thread: newId(), turns: [], surfaces: {} })}
          icon={<Plus size={20} aria-hidden="true" />}>{t("asst.new_chat")}</Button>} />
      <div className="mc-surface flex h-[calc(100vh-16rem)] min-h-96 flex-col rounded-2xl border border-aeblack-100 bg-whitely-50">
        <div className="flex-1 space-y-4 overflow-y-auto p-4 sm:p-6" aria-live="polite">
          {visible.length === 0 && <Note>{t("asst.examples")}</Note>}
          {visible.map((m) => (
            <div key={m.id} className={`flex ${m.role === "user" ? "justify-end" : "justify-start"}`}>
              <div className={`max-w-[92%] rounded-2xl px-4 py-3 text-sm leading-6 sm:max-w-[85%] ${m.role === "user" ? "mh-chat-user" : "mh-chat-bot w-full"}`}>
                {m.role === "assistant" && <ChatCircleDots size={20} className="mb-1 text-primary-600" aria-hidden="true" />}
                {m.tools && m.tools.length > 0 && (
                  <ul className="mb-2 flex flex-wrap gap-2" aria-label={t("asst.thinking")}>
                    {m.tools.map((x) => (
                      <li key={x.id} className="inline-flex items-center gap-1 rounded-full bg-whitely-100 px-3 py-1 text-xs text-aeblack-600">
                        {x.done ? <CheckCircle size={16} className="text-aegreen-600" aria-hidden="true" /> : <CircleNotch size={16} className="animate-spin" aria-hidden="true" />}
                        {t(`asst.tool.${x.name}`) === `asst.tool.${x.name}` ? x.name : t(`asst.tool.${x.name}`)}
                      </li>
                    ))}
                  </ul>
                )}
                {m.role === "user" ? <p className="whitespace-pre-line" dir="auto">{m.text}</p> : <Markdown text={m.text} />}
                {m.surfaces?.map((id) => state.surfaces[id] && <A2Surface key={id} surface={state.surfaces[id]} busy={acting === id} onAction={onAction} emptyText={t("common.choose")}
                  onData={(sid, path, value) => setState((s) => ({ ...s, surfaces: writeData(s.surfaces, sid, path, value) }))} />)}
              </div>
            </div>
          ))}
          {running && <p className="flex items-center gap-2 text-sm text-aeblack-500"><CircleNotch size={20} className="animate-spin" aria-hidden="true" />{t("asst.thinking")}</p>}
          {error && <ErrorState error={error} />}
          <div ref={end} />
        </div>
        <form onSubmit={submit} className="flex items-end gap-2 border-t border-aeblack-100 bg-white p-3">
          <label htmlFor="asst-q" className="sr-only">{t("asst.placeholder")}</label>
          <textarea id="asst-q" rows={2} dir="auto" className="flex-1 resize-none rounded-xl border border-aeblack-200 p-3 outline-none focus:border-primary-500"
            placeholder={t("asst.placeholder")} value={text} onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); (e.currentTarget.form as HTMLFormElement).requestSubmit(); } }} />
          {running
            ? <Button variant="outline" onClick={() => stop.current?.abort()} icon={<StopCircle size={24} aria-hidden="true" />}>{t("asst.stop")}</Button>
            : <Button type="submit" disabled={!text.trim()} icon={<ArrowCircleUp size={24} aria-hidden="true" />}>{t("asst.send")}</Button>}
        </form>
      </div>
    </>
  );
}
