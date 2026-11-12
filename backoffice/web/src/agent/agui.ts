import { ApiError } from "../core";

/** AG-UI (https://docs.ag-ui.com) client for the recruitment agent, reached through /api/backoffice/agent/run. */

export type AgMessage = { id: string; role: "user" | "assistant"; content: string };

export type AgEvent =
  | { type: "RUN_STARTED"; threadId: string; runId: string }
  | { type: "RUN_FINISHED"; threadId: string; runId: string }
  | { type: "RUN_ERROR"; message: string; code?: string }
  | { type: "TEXT_MESSAGE_START"; messageId: string; role?: string }
  | { type: "TEXT_MESSAGE_CONTENT"; messageId: string; delta: string }
  | { type: "TEXT_MESSAGE_END"; messageId: string }
  | { type: "TOOL_CALL_START"; toolCallId: string; toolCallName: string; parentMessageId?: string }
  | { type: "TOOL_CALL_ARGS"; toolCallId: string; delta: string }
  | { type: "TOOL_CALL_END"; toolCallId: string }
  | { type: "TOOL_CALL_RESULT"; toolCallId: string; messageId?: string; content: string }
  | { type: string; [key: string]: unknown };

export const newId = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`);

/** Starts a run and calls onEvent for every AG-UI event until the run ends. */
export async function runAgent(threadId: string, messages: AgMessage[], lang: string, onEvent: (e: AgEvent) => void, signal?: AbortSignal) {
  let res: Response;
  try {
    res = await fetch("/api/backoffice/agent/run", {
      method: "POST",
      credentials: "same-origin",
      signal,
      headers: { "Content-Type": "application/json", Accept: "text/event-stream", "X-Backoffice": "1", "X-Lang": lang },
      body: JSON.stringify({ threadId, runId: newId(), messages, tools: [], context: [], state: {}, forwardedProps: {} }),
    });
  } catch (e) {
    if ((e as Error).name === "AbortError") return;
    throw new ApiError("network", 0);
  }
  if (res.status === 401) {
    window.dispatchEvent(new Event("mh:signed-out"));
    throw new ApiError("signed_out", 401);
  }
  if (!res.ok || !res.body) {
    let code = res.status === 403 ? "assistant_off" : res.status === 429 ? "too_many_messages" : "server";
    try {
      code = (await res.json())?.error?.code || code;
    } catch {
      /* not JSON */
    }
    throw new ApiError(code, res.status);
  }

  const reader = res.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += value.replace(/\r\n/g, "\n");
    let cut: number;
    while ((cut = buffer.indexOf("\n\n")) >= 0) {
      const block = buffer.slice(0, cut);
      buffer = buffer.slice(cut + 2);
      const data = block.split("\n").filter((l) => l.startsWith("data:")).map((l) => l.slice(5).trimStart()).join("\n");
      if (!data) continue; // keepalive comments
      try {
        onEvent(JSON.parse(data));
      } catch {
        /* ignore a malformed event */
      }
    }
  }
}

/** Tool results may arrive as JSON text or as JSON text inside a JSON string. */
export function parseToolResult(content: string): any {
  let v: any = content;
  for (let i = 0; i < 2 && typeof v === "string"; i++) {
    try {
      v = JSON.parse(v);
    } catch {
      break;
    }
  }
  return v;
}
