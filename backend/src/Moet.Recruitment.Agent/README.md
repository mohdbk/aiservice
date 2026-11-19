# Recruitment Agent

The recruitment assistant in the backoffice: a library the API runs inside its own process (no separate service). Made by the Ministry of Economy & Tourism.

- **Microsoft Agent Framework** (`Microsoft.Agents.AI`): a `ChatClientAgent` with 20 tools, using any OpenAI-compatible model.
- **AG-UI**: the API serves it at `/api/backoffice/agent/run` with `MapAGUIServer`. Each run streams text, tool calls and tool results as server-sent events.
- **A2UI v1.0**: the show and propose tools return A2UI surfaces (applicant cards, comparisons, approval cards) inside their tool results. The backoffice draws them with UAE Design System styles and sends button events back.

## How a request flows

```
Browser (backoffice)  --AG-UI-->  API /api/backoffice/agent/run  (staff sign-in, rate limit, the agent runs here)
Browser  <--events (text, tool calls, A2UI)--
Agent tools  --staff member's own session, in memory-->  API /api/backoffice/*  (role checks, audit trail)
```

- The agent holds no credentials. Each tool call goes to the backoffice API with the signed-in staff member's session, so it can see and do only what that person can.
- **Read tools:** jobs, applicants (including a search of CV text), interviews, offers, tests, test results, lookups, dashboard.
- **Show tools:** `show_applicants`, `compare_applicants`.
- **Propose tools:** status change, note, interview, test invitation, draft offer, candidate email, draft job, English suggestion.
  - The backend stores the proposal (`ops.AgentProposal`) and writes its description from current data.
  - The proposal is carried out only when the same person presses **Approve**, once, before it expires (30 minutes by default; set in Assistant Settings).
  - The change runs through the normal backoffice services, so it is audited like any other change.

## Forms, components and the open screen

- **ask_user:** when the agent needs details, it shows an A2UI form instead of asking in text. Your answers come back to it as a `submit_form` action.
- **Approval cards:** each card is an editable form. On Approve, the backend applies only the fields that action allows, and checks them again.
- **Components** (Setup › Assistant Components): admins design question forms and approval cards in a visual builder, with a live preview in Arabic and English. The agent finds them with `list_components` and shows them with `show_component`. A designed approval card replaces the built-in card for its action.
- **Open screen:** the backoffice sends the page and record that are open (for example `applicant_id: 12`) with each run. It travels in the AG-UI `context` field and in the `X-Agent-Context` header. The assistant opens as a side panel on every page.

## Assistant Settings (Setup)

Admins can set the following; the agent reads them on each run, with a 30-second cache:

- **On or off.**
- **Model:** server address, model name, and access key. The key is stored encrypted with AES-GCM, using a key derived from `AGENT_KEY`.
- **Behaviour:** temperature, maximum tool steps per question, and extra guidance.
- **Tools:** which tools are switched on.
- **Limits:** how long an approval card stays valid, questions per person per day, and how many messages are remembered.
- **Example questions:** shown at the start of a conversation.

The agent fetches these from `/api/internal/agent/settings`, which accepts only the shared `AGENT_KEY`.

## Environment variables

| Variable | Purpose |
|---|---|
| `AGENT_KEY` | At least 16 characters; protects the assistant's loopback endpoint and encrypts saved secrets |
| `LLM_BASE_URL`, `LLM_MODEL`, `LLM_API_KEY` | The API's model, used by the assistant too (OpenAI-compatible with tool calling: on-prem vLLM, Ollama, Azure OpenAI and so on) |
| `AGENT_LLM_BASE_URL`, `AGENT_LLM_MODEL`, `AGENT_LLM_API_KEY` | Optional: a different model for the assistant |

The assistant can be switched off under Setup › Assistant Settings.

## On-prem

- It runs inside the API (and so inside the backoffice site on IIS): there is nothing to deploy on its own.
- It keeps no data. Conversations stay in the staff member's browser, and proposals and changes are stored by the API in SQL Server.

## Tests

`dotnet test backend/tests/Moet.Recruitment.Agent.Tests` runs the real AG-UI endpoint with a scripted model and a fake backoffice. The tests check:

- the tool calls and the A2UI approval surface in the event stream;
- that the staff member's session is passed on with each tool call;
- that calls without the agent key are refused.
