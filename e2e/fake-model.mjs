// A stand-in for an OpenAI-compatible model server, for end-to-end checks without a real model. The first turn asks
// for the list_jobs tool; once a tool result is in the conversation it replies with text. Streams like the real API.
import http from "node:http";

const chunk = (delta, finish = null) => `data: ${JSON.stringify({ id: "c1", object: "chat.completion.chunk", created: 0, model: "fake", choices: [{ index: 0, delta, finish_reason: finish }] })}\n\n`;

http.createServer(async (req, res) => {
  let body = "";
  for await (const c of req) body += c;
  if (!req.url.endsWith("/chat/completions")) { res.writeHead(404).end(); return; }
  const input = JSON.parse(body || "{}");
  const hasTool = (input.messages || []).some((m) => m.role === "tool");
  const wantsTools = Array.isArray(input.tools) && input.tools.length > 0;
  if (!input.stream) {
    const message = hasTool || !wantsTools
      ? { role: "assistant", content: "Fake reply: there are open jobs." }
      : { role: "assistant", content: null, tool_calls: [{ id: "call_1", type: "function", function: { name: "list_jobs", arguments: "{}" } }] };
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ id: "c1", object: "chat.completion", created: 0, model: "fake", choices: [{ index: 0, message, finish_reason: message.tool_calls ? "tool_calls" : "stop" }], usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 } }));
    return;
  }
  res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
  if (hasTool || !wantsTools) {
    res.write(chunk({ role: "assistant", content: "" }));
    for (const w of ["Fake ", "reply: ", "there ", "are ", "open ", "jobs."]) res.write(chunk({ content: w }));
    res.write(chunk({}, "stop"));
  } else {
    res.write(chunk({ role: "assistant", content: null, tool_calls: [{ index: 0, id: "call_1", type: "function", function: { name: "list_jobs", arguments: "" } }] }));
    res.write(chunk({ tool_calls: [{ index: 0, function: { arguments: "{}" } }] }));
    res.write(chunk({}, "tool_calls"));
  }
  res.end("data: [DONE]\n\n");
}).listen(9000, () => console.log("fake model on :9000"));
