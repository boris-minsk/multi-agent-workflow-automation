# MultiAgent — AI Sales Ops Assistant

A multi-agent workflow built with **Microsoft Agent Framework** (`Microsoft.Agents.AI`),
.NET 10, and ASP.NET Core. Demonstrates the pattern a recruiter would expect from an
"AI Sales Ops" build: lead qualification, research, outreach drafting, CRM updates, and
operational monitoring — all coordinated through a deterministic .NET pipeline with
agent-by-agent retry, tracing, and structured output.

This is the **Phase 1 / "Cheap MVP"** build from
[`docs/plans/phase1-cheap-mvp-multi-agent-sales-assistant.md`](docs/plans/phase1-cheap-mvp-multi-agent-sales-assistant.md):
external SaaS (HubSpot, Gmail, Slack, Notion) is replaced with local mock adapters so
the project demonstrates architecture, prompt engineering, and orchestration without
OAuth setup or per-vendor accounts.

---

## Workflow

```
        HTTP POST /api/leads/{id}/run
                  │
                  ▼
         ┌────────────────────┐
         │ WorkflowRunner     │   enqueues to a durable background worker
         │  + Polly retry     │   tracing decorator wraps every agent call
         │  + AgentTracer     │
         └────────┬───────────┘
                  │
                  ▼
        ┌─────────────────────────┐
        │ LeadQualificationAgent  │ → LeadScore { Score, Priority, Reason, … }
        └────────┬────────────────┘
                  │ score ≥ threshold?
        ┌─────────┴─────────┐
       NO                  YES
        │                   ▼
        │         ┌─────────────────────┐
        │         │ ResearchAgent       │ → ResearchSummary (uses pre-fetched
        │         │                     │    company info + CRM history)
        │         └──────────┬──────────┘
        │                    ▼
        │         ┌─────────────────────┐
        │         │ OutreachAgent       │ → OutreachDraft { Subject, Body, NextAction }
        │         └──────────┬──────────┘
        │                    ▼
        │         ┌─────────────────────┐
        │         │ FileSystemEmail     │ writes outbox/{runId}.eml
        │         │ Sender              │
        │         └──────────┬──────────┘
        │                    ▼
        └──────────┐ ┌───────────────────┐
                   ▼ │ CrmUpdateAgent    │ → CrmUpdateInstructions
                     │                   │ workflow applies: stage, note,
                     │                   │ markdown note → SQLite + notes/*.md
                     └──────────┬────────┘
                                ▼
                     ┌────────────────────┐
                     │ ConsoleNotification│ console log + Notifications table
                     │ Sink ("Slack")     │
                     └────────────────────┘

Monitoring/Recovery role is cross-cutting, not an extra agent:
  - Polly retry (3 attempts, exponential backoff + jitter) on every agent call
  - One AgentTrace row per attempt (input/output/duration/retries)
  - JSON-parse failures get one in-place retry before counting against the budget
  - Terminal failure posts a red notification to the "Slack" sink
```

## Solution layout

```
src/
  MultiAgent.Core/             domain models, abstractions (no Semantic Kernel [Microsoft's AI orchestration SDK], no EF, no AI deps)
  MultiAgent.Agents/           ChatClientAgent wrappers, prompts, runner, workflow
  MultiAgent.Infrastructure/   SQLite (EF Core), file email, console notifications,
                               markdown notes, mock LLM, OpenAI IChatClient
  MultiAgent.Api/              ASP.NET Core Web API + minimal vanilla-JS UI
tests/
  MultiAgent.Tests/            xUnit — mock generator unit tests + workflow integration
data/
  seed-leads.json              10 sample leads spanning industries and intent levels
  seed-research.json           matching company-research context
```

The five "agents" each wrap a `ChatClientAgent` (Microsoft Agent Framework):

| Agent                                      | Output                  | What it does                                                    |
|---                                         |-------------------------|-----------------------------------------------------------------|
| `LeadQualificationAgent`                   | `LeadScore`             | Scores 1–10, infers industry, picks Priority, gives reason      |
| `ResearchAgent`                            | `ResearchSummary`       | Synthesizes pre-fetched company + CRM data into a brief         |
| `OutreachAgent`                            | `OutreachDraft`         | Drafts a <150-word personalized email                           |
| `CrmUpdateAgent`                           | `CrmUpdateInstructions` | Decides stage + structured notes; workflow executes             |
| Monitoring/Recovery                        | _cross-cutting_         | Polly retries, traces every attempt, alerts on terminal failure |

## Tech stack

- **.NET 10** (LTS) — pinned via `global.json`
- **Microsoft Agent Framework 1.7.0** (`Microsoft.Agents.AI` / `ChatClientAgent` / `AIAgent`) —
  Microsoft's unified successor to Semantic Kernel Agents and AutoGen
- **Microsoft.Extensions.AI 10.6.0** (`IChatClient`) — provider-agnostic chat abstraction
- **OpenAI** SDK — real-LLM path, default model `gpt-4o-mini`
- **EF Core 10 + SQLite** — persistence for leads, runs, traces, outbox, notifications
- **Polly v8** (`ResiliencePipeline`) — retry with exponential backoff + jitter
- **Serilog** — structured logs to console + `logs/log-.txt` (daily rolling)
- **xUnit** — tests

## Running it

### Prerequisites
- .NET 10 SDK
- (optional) OpenAI API key — without one, the app runs in mock mode

### Build & test

```powershell
dotnet build
dotnet test
```

### Run with the mock LLM (no API key needed)

```powershell
dotnet run --project src/MultiAgent.Api
```

Default port: `http://localhost:5032`. Open it in a browser — you'll see the dashboard
with 10 seeded leads. Click "Run workflow" on any of them. The mock generates
plausible canned responses with no network calls. Total cost: zero.

### Run with real OpenAI

```powershell
$env:Llm__Provider = "OpenAI"
$env:OpenAI__ApiKey = "sk-..."
dotnet run --project src/MultiAgent.Api
```

The default model is `gpt-4o-mini` (~$0.15 / 1M input tokens). A full workflow run
through all four agents costs well under a US cent.

### What you'll see

- **Leads panel** — 10 seeded leads spanning Logistics, Biotech, Finance, F&B, Robotics,
  Education, Renewables, Apparel, Healthcare, Outdoor Retail. CRM notes range from
  explicit demo requests with budget approval ("high intent") to free-tier users with
  no engagement ("low intent").
- **Workflow runs** — click a run to expand its agent-by-agent trace timeline with
  per-step input, output, duration, and retry count.
- **Outbox** — generated emails as both DB rows and `.eml` files on disk. With
  `Email:Provider=Smtp` the same draft is actually sent (Gmail via app password) and the
  `.eml` becomes the send audit; the default `File` provider only writes the `.eml`.
- **Notifications** — the "Slack" feed with info/warn/error entries.
- **Notes** — per-lead markdown files in `notes/{leadId}.md` (mocks Notion).

### Human approval (HITL)

By default a qualifying lead's outreach email is **held for human approval** before it is
sent — a real system does not auto-send AI-written emails to prospects. When a run reaches
the send step it pauses with status **AwaitingApproval** and shows an inline panel in the
Workflow runs column: edit the subject/body if you want, then **Approve & send** or
**Reject**. Approve resumes the run (send → CRM update → Completed); reject ends it as
**Rejected** with no email and a CRM note (the lead stays qualified — only this email was
declined).

`Workflow:ApprovalMode` controls when the gate applies:

| Mode | Behavior |
|---|---|
| `Always` (default) | Every qualifying lead waits for approval |
| `HighValueOnly` | Only High-priority or score ≥ 8 leads wait; routine leads auto-send |
| `Never` | No gate — send automatically (the original behavior) |

It works in mock mode too (approving is a human click, not an LLM call), and the pause
state is stored on the run row, so an approval survives an app restart. Endpoints:
`POST /api/runs/{id}/approve` (optional `{ subject, body }`) and
`POST /api/runs/{id}/reject` (optional `{ reason }`).

### Configuration

`src/MultiAgent.Api/appsettings.json` (env-var overrides via `__`):

```jsonc
{
  "Llm": {
    "Provider": "Mock",            // "Mock" | "OpenAI"
    "Model": "gpt-4o-mini",
    "Temperature": 0.3,
    "MaxTokens": 1500,
    "MockThrowOnAgent": null       // for failure-injection in tests
  },
  "OpenAI": { "ApiKey": "" },      // set via OpenAI__ApiKey env var
  "Email": {
    "Provider": "File"             // "File" (.eml mock) | "Smtp" (real send)
  },
  "Smtp": {                        // used when Email:Provider = "Smtp"; Gmail-ready defaults
    "Host": "smtp.gmail.com",
    "Port": 587,                   // 587 ⇒ STARTTLS; 465 ⇒ set UseStartTls false
    "UseStartTls": true,
    "Username": "",                // Gmail address; via Smtp__Username
    "Password": "",                // Gmail App Password; via Smtp__Password
    "FromAddress": "",             // defaults to Username
    "FromName": "AI Sales Ops Assistant"
  },
  "Workflow": {
    "QualificationThreshold": 5,   // score < threshold ⇒ skip outreach
    "MaxRetries": 3,
    "RetryBaseDelayMs": 500,
    "ApprovalMode": "Always"       // "Always" | "HighValueOnly" | "Never" — human email approval
  },
  "Paths": {
    "OutboxDirectory": "outbox",
    "NotesDirectory": "notes",
    "SqliteDb": "data/multiagent.db"
  }
}
```

All paths resolve relative to `AppContext.BaseDirectory` (the output folder) so the
runtime files live next to the binary, regardless of how you launch the app.

## API surface

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/health` | reports LLM provider + model |
| `GET` | `/api/leads` | list seeded leads |
| `GET` | `/api/leads/{id}` | single lead |
| `POST` | `/api/leads/{id}/run` | start workflow; returns `{ runId }` (202 Accepted) |
| `GET` | `/api/runs?take=20` | list recent runs |
| `GET` | `/api/runs/{id}` | run + full agent trace |
| `POST` | `/api/runs/{id}/approve` | approve a run awaiting approval (optional `{ subject, body }` edit); resumes the send. 409 if not awaiting |
| `POST` | `/api/runs/{id}/reject` | reject a run awaiting approval (optional `{ reason }`); no email sent |
| `GET` | `/api/outbox?take=20` | generated emails (subject + body) |
| `GET` | `/api/outbox/{id}` | single email |
| `GET` | `/api/notifications?take=50` | "Slack" feed |
| `GET` | `/openapi/v1.json` | OpenAPI spec (Development only) |

## Why these design choices

**Why Microsoft Agent Framework over Semantic Kernel Agents.** Microsoft began unifying
SK Agents + AutoGen into `Microsoft.Agents.AI` (GA, currently 1.7.0). Agent Framework
exposes a cleaner API (`new ChatClientAgent(chatClient, options)`), uses provider-agnostic
`IChatClient`, and is the path the SK migration guide points to. SK Agents still works
but you'd plan to migrate; starting on Agent Framework avoids that.

**Why a custom workflow runner over `AgentWorkflowBuilder.BuildSequential`.** The
pipeline needs conditional skip (low score → no outreach), per-agent retry, and
per-step tracing. A custom runner with explicit C# control flow is clearer for those
than fighting a graph builder. `BuildSequential` (and the richer `WorkflowBuilder`)
remain swappable. Phase 3's HITL email approval was therefore built **on the custom
runner** as a durable pause/resume (state saved on the run row) rather than on the native
`ApprovalRequiredAIFunction` flow — the native path gates a *tool call* (the email send
isn't a tool here), needs a live event-stream reader + checkpoint storage, is OpenAI-only,
and would drop the conditional skip + per-step tracing. See "Human approval" above.

**Why no tool calling in Phase 1.** The agents are pure functions (input → structured
output). The workflow owns I/O — fetching company info, sending emails, updating CRM,
writing notes. This makes the mock trivial, tests deterministic, and the control flow
obvious. Phase 2 will introduce one tool-using agent for the portfolio talking point.

**Why a mock LLM alongside real OpenAI.** Three reasons: (1) tests run without an API
key, (2) the demo runs at zero cost for someone exploring the repo, (3) the mock's
heuristic scoring lets us seed leads spanning intent levels and watch the
qualification + skip branch work end-to-end. The mock's behavior is documented in
`MockResponseGenerator` — keyword-driven scoring with deterministic JSON output.

## Phase 2 / Phase 3 roadmap

Phase 2 — one real integration:
- Gmail via SMTP app password (lowest OAuth burden) **or** HubSpot via private app token
- `docker-compose.yml` with **n8n** running one scheduled workflow that polls `/api/leads`
  and triggers `/api/leads/{id}/run` for any new lead
- Expose CRM operations as an **MCP server** (C# `ModelContextProtocol` SDK) so Claude
  Desktop / Cursor can use the same tools

Phase 3 — production-readiness:
- ✅ **HITL email approval (built)** — a qualifying lead's email pauses for human
  approve/edit/reject before sending; see "Human approval" above. Built as a durable
  pause/resume on the custom runner; `Workflow:ApprovalMode` = Always | HighValueOnly | Never.
- ✅ **Durable in-flight runs (built)** — a Channel-backed `WorkflowQueue` + a `WorkflowWorker`
  background service replace the in-memory `Task.Run`; on startup, recovery re-queues unfinished
  work and resumes interrupted runs that hadn't sent yet (never double-sending). `Workflow:MaxConcurrentRuns` tunes the worker.
- Prometheus metrics endpoint, Docker Compose deployment, **API authentication**

## Repository tour

```
docs/Multi_agent_workflow_automation.md                  the original brief
docs/plans/phase1-cheap-mvp-multi-agent-sales-assistant.md  this build's plan

src/MultiAgent.Core/
  Models/                  domain records and entities
  Abstractions/            ICrmRepository, IEmailSender, INotificationSink, INotesStore,
                           ICompanyResearchSource, IAgentTracer, IWorkflowRunStore,
                           IWorkflowRunner

src/MultiAgent.Agents/
  Prompts/                 embedded *.system.md per agent
  Runner/                  IAgentRunner, AgentRunner, TracingAgentRunner (retry + trace)
  Agents/                  the four ChatClientAgent wrappers
  Workflow/                SalesFollowUpWorkflow + WorkflowRunner

src/MultiAgent.Infrastructure/
  Persistence/             AppDbContext, EF migrations, Sqlite* implementations
  Email/                   FileSystemEmailSender (.eml mock) + SmtpEmailSender (real SMTP,
                           Gmail-ready) selected by Email:Provider; ISmtpDispatcher seam
  Notifications/           ConsoleNotificationSink — ILogger + Notifications table
  Notes/                   MarkdownNotesStore — writes per-lead .md files
  Llm/                     IChatClient registration, MockChatClient, MockResponseGenerator

src/MultiAgent.Api/
  Endpoints/               minimal-API endpoint maps per resource
  wwwroot/                 vanilla-JS dashboard (index.html, styles.css, app.js)

tests/MultiAgent.Tests/
  Llm/                     MockResponseGenerator heuristic tests
  Workflow/                end-to-end pipeline tests (happy/skip/retry-failure paths)
  TestFixtures/            disposable DI fixture with temp SQLite
```
