# MultiAgent.Mcp — CRM tools over MCP

A stdio [Model Context Protocol](https://modelcontextprotocol.io) server that exposes the
project's CRM operations as tools any MCP client (Claude Desktop, Cursor, Claude Code) can call.
It reuses the same `ICrmRepository` the API uses, so it talks to the mock SQLite CRM or the real
HubSpot CRM depending on `Crm:Provider` — the same switch the root README documents.

## Tools

| Tool | Description |
|------|-------------|
| `GetLead(leadId)` | Fetch a lead/contact by GUID, returned as JSON. |
| `UpdateLeadStage(leadId, stage)` | Move a lead to `New` / `Qualified` / `Contacted` / `Replied` / `Disqualified`. |
| `AddCrmNote(leadId, note)` | Append a one-line activity note. |

These are the same three operations the in-app `CrmUpdateAgent` calls as function tools — here
they are reachable from outside the app.

## Run it

Mock CRM (no account needed):

```bash
dotnet run --project src/MultiAgent.Mcp
```

Real HubSpot — set the provider + a private-app token first (never commit it):

```bash
Crm__Provider=HubSpot HubSpot__AccessToken=pat-... dotnet run --project src/MultiAgent.Mcp
```

For HubSpot, the token's scopes determine what works: `crm.objects.contacts.read` (GetLead),
`crm.objects.contacts.write` (UpdateLeadStage → `hs_lead_status`), `crm.objects.notes.write` (AddCrmNote).

## Use from an MCP client

The repo ships a project-level [`.mcp.json`](../../.mcp.json) registering this server in mock mode,
so Cursor / Claude Code pick it up automatically. Build once before first use so the client's
`dotnet run` launch is fast and quiet:

```bash
dotnet build src/MultiAgent.Mcp
```

For HubSpot mode in a client, add an `env` block to the server entry:

```json
{
  "servers": {
    "multiagent-crm": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "src/MultiAgent.Mcp"],
      "env": { "Crm__Provider": "HubSpot", "HubSpot__AccessToken": "pat-..." }
    }
  }
}
```

> **stdio note:** the protocol uses stdout for JSON-RPC, so all logging goes to stderr.
