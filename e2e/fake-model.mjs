// A stand-in for an OpenAI-compatible model server, for end-to-end checks without a real model. The first turn asks
// for the list_jobs tool; once a tool result is in the conversation it replies with text. Streams like the real API.
import http from "node:http";

const chunk = (delta, finish = null) => `data: ${JSON.stringify({ id: "c1", object: "chat.completion.chunk", created: 0, model: "fake", choices: [{ index: 0, delta, finish_reason: finish }] })}\n\n`;

http.createServer(async (req, res) => {
  let body = "";
  for await (const c of req) body += c;
  if (!req.url.endsWith("/chat/completions")) { res.writeHead(404).end(); return; }
  const input = JSON.parse(body || "{}");
  if (process.env.SCENARIO && Array.isArray(input.tools) && input.tools.length > 0) return scenario(input, res);
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

// SCENARIO=1 (design audit): the last user message picks a short plan of tool calls, so the chat shows every kind of
// card the assistant can draw. Ids come from the previous tool result.
const PLANS = [
  [/compare|قارن/i, [["search_applicants", () => ({})], ["compare_applicants", (ids) => ({ applicantIds: ids.slice(0, 3) })]],
    "**Comparison ready.** The first applicant has the strongest CV match; check the personality fit before the interview."],
  [/cards|بطاقات/i, [["search_applicants", () => ({})], ["show_applicants", (ids) => ({ applicantIds: ids.slice(0, 4) })]],
    "**Cards ready.**\n\n- 4 applicants shown\n- 2 have completed the personality questionnaire"],
  [/form|نموذج/i, [["ask_user", () => ({ title: "Interview details", fields: [
    { name: "date", label: "Date", type: "date", required: true },
    { name: "round", label: "Round", type: "choice", options: [{ value: "1", label: "Technical" }, { value: "2", label: "HR" }], required: true },
    { name: "minutes", label: "Length (minutes)", type: "number", value: "45" },
    { name: "notes", label: "Notes", type: "longtext" },
    { name: "notify", label: "Email the candidate", type: "checkbox", value: "true" },
  ] })]], "**Form shown.** Fill it in and press Submit."],
  [/note|ملاحظة/i, [["search_applicants", () => ({})], ["propose_note", (ids) => ({ applicantId: ids[0], text: "Strong Power BI experience; invite to the technical interview." })]],
    "**Proposal ready.** It is waiting for your approval."],
];

function scenario(input, res) {
  const msgs = input.messages || [];
  let lastUser = msgs.length - 1;
  while (lastUser >= 0 && msgs[lastUser].role !== "user") lastUser--;
  const text = typeof msgs[lastUser]?.content === "string" ? msgs[lastUser].content : JSON.stringify(msgs[lastUser]?.content || "");
  const plan = PLANS.find(([re]) => re.test(text)) || [null, [["list_jobs", () => ({})]], "Here are the open jobs."];
  const done = msgs.slice(lastUser + 1).filter((m) => m.role === "tool");
  const last = done.at(-1)?.content;
  const raw = (typeof last === "string" ? last : JSON.stringify(last ?? "")).replace(/\\"/g, '"');
  const ids = [...raw.matchAll(/"id"\s*:\s*(\d+)/g)].map((m) => Number(m[1]));
  const step = plan[1][done.length];
  console.log(`scenario: "${text.slice(0, 40)}" step ${done.length} ids ${ids.slice(0, 4)} stream ${!!input.stream} -> ${step ? step[0] : "text"}`);
  if (!input.stream) {
    const message = step
      ? { role: "assistant", content: null, tool_calls: [{ id: `call_${done.length + 1}`, type: "function", function: { name: step[0], arguments: JSON.stringify(step[1](ids)) } }] }
      : { role: "assistant", content: plan[2] };
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ id: "c1", object: "chat.completion", created: 0, model: "fake", choices: [{ index: 0, message, finish_reason: step ? "tool_calls" : "stop" }], usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 } }));
    return;
  }
  res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
  if (step) {
    const [name, args] = step;
    res.write(chunk({ role: "assistant", content: null, tool_calls: [{ index: 0, id: `call_${done.length + 1}`, type: "function", function: { name, arguments: "" } }] }));
    res.write(chunk({ tool_calls: [{ index: 0, function: { arguments: JSON.stringify(args(ids)) } }] }));
    res.write(chunk({}, "tool_calls"));
  } else {
    res.write(chunk({ role: "assistant", content: "" }));
    res.write(chunk({ content: plan[2] }));
    res.write(chunk({}, "stop"));
  }
  res.end("data: [DONE]\n\n");
}
