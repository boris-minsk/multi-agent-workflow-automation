# n8n scheduled lead runner

A scheduled n8n workflow that polls the Sales Ops API for **new** leads and kicks
off the multi-agent run for each one:

```
Every 15 min ─▶ GET {API_BASE_URL}/api/leads ─▶ keep stage == "New" ─▶ POST /api/leads/{id}/run
```

`stage == "New"` makes it idempotent: a completed run always moves the lead off
`New` (to `Contacted` / `Replied` / `Disqualified`), so it's never re-processed.

- **Workflow export:** [`sales-ops-lead-runner.json`](sales-ops-lead-runner.json)
- **Compose file:** [`../../docker-compose.yml`](../../docker-compose.yml)
- The HTTP nodes read the API base URL from the `API_BASE_URL` env var via the
  expression `{{ $env.API_BASE_URL }}`, so the same export works against either topology.

## Prerequisites

- Docker Desktop running.
- `.env` created from [`../../.env.example`](../../.env.example) (optional — defaults work for dev).

---

## Topology A — dev (default): API on the host, n8n in Docker

The API stays native so you keep the `dotnet run` / F5-debug inner loop; only n8n
is containerized.

1. **Start the API bound to all interfaces** (not just `localhost`, or the n8n
   container can't reach it through `host.docker.internal`):
   ```powershell
   $env:ASPNETCORE_URLS = "http://+:5032"
   dotnet run --project src/MultiAgent.Api
   ```
   (Windows Firewall may prompt once to allow inbound on the dotnet process — allow it.)

2. **Start n8n:**
   ```powershell
   docker compose up -d
   ```

3. Open <http://localhost:5678>, create the owner account (first launch only).

4. **Import the workflow:** Workflows → ⋯ → *Import from File* → pick
   `docs/n8n/sales-ops-lead-runner.json`. (Or via CLI — see below.)

5. Click **Execute Workflow** to fire it immediately, or toggle it **Active** to
   run on the 15-minute schedule. Watch the runs land in the dashboard at
   <http://localhost:5032>.

---

## Topology B — full (prod-shaped): API also containerized

The API runs as its own container; n8n reaches it by service name on the internal
Docker network.

```powershell
$env:API_BASE_URL = "http://api:8080"
docker compose --profile full up -d --build
```

- n8n: <http://localhost:5678> · API/dashboard: <http://localhost:8080>
- Writable state (SQLite, outbox, notes) lives on the `multiagent-runtime` volume;
  read-only seed JSONs are baked into the image at `/app/data`.
- Defaults to Mock LLM + Sqlite CRM (no credentials). To use real providers,
  uncomment the `api` env block in `docker-compose.yml` and supply secrets via `.env`.

> In **real** production you'd drop the published `8080:8080` port (reach `api` only
> over the internal network, behind a reverse proxy/ingress) and add API auth.

---

## Import via CLI (instead of the UI)

The Compose file mounts this folder read-only at `/workflows` in the n8n container:

```powershell
docker compose exec n8n n8n import:workflow --input=/workflows/sales-ops-lead-runner.json
```

> The n8n CLI can **import** a schedule-triggered workflow but can't **execute** one
> directly (`n8n execute` needs a manual start node). To run it, use the UI **Execute
> Workflow** button or **activate** it and let the schedule fire.

## Changing the schedule

Edit the **Every 15 min** node (Schedule Trigger) in the n8n editor, or change
`minutesInterval` in `sales-ops-lead-runner.json` before importing.

## Troubleshooting

- **`Get Leads` connection refused (dev):** the host API isn't listening on all
  interfaces. Restart it with `ASPNETCORE_URLS=http://+:5032` (step 1 above).
- **`$env.API_BASE_URL` is empty:** ensure `N8N_BLOCK_ENV_ACCESS_IN_NODE=false`
  (already set in `docker-compose.yml`) and re-create the container after changing env.
- **Re-running the same leads does nothing:** expected — they've left the `New`
  stage. Re-seed (delete the SQLite DB / volume) or add a fresh lead to see a run.
