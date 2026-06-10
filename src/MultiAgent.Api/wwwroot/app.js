// MultiAgent dashboard — vanilla JS, no framework.
// Polls runs every 2s while any run is active; loads everything else on user action.

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => document.querySelectorAll(sel);

const State = {
  leads: [],
  runs: [],
  outbox: [],
  notifications: [],
  expandedRuns: new Set(),
  pollTimer: null
};

async function api(path, options) {
  const res = await fetch(path, options);
  if (!res.ok) throw new Error(`${path} → ${res.status} ${res.statusText}`);
  return res.json();
}

function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function timeAgo(iso) {
  if (!iso) return '';
  const ms = Date.now() - new Date(iso).getTime();
  if (ms < 60_000) return Math.max(0, Math.floor(ms / 1000)) + 's ago';
  if (ms < 3_600_000) return Math.floor(ms / 60_000) + 'm ago';
  if (ms < 86_400_000) return Math.floor(ms / 3_600_000) + 'h ago';
  return Math.floor(ms / 86_400_000) + 'd ago';
}

// ---------- Health ----------
async function loadHealth() {
  try {
    const h = await api('/api/health');
    const badge = $('#provider-badge');
    badge.textContent = h.llmProvider + ' · ' + h.llmModel;
    badge.classList.add(h.llmProvider.toLowerCase());
  } catch (e) {
    $('#provider-badge').textContent = 'offline';
  }
}

// ---------- Leads ----------
async function loadLeads() {
  State.leads = await api('/api/leads');
  $('#lead-count').textContent = State.leads.length;
  $('#leads-list').innerHTML = State.leads.map(renderLead).join('') ||
    '<div class="empty">no leads</div>';
  $$('.run-workflow-btn').forEach((b) =>
    b.addEventListener('click', (e) => runWorkflow(e.target.dataset.leadId, e.target)));
}

function renderLead(l) {
  const priority = (l.priority || '').toLowerCase();
  const scorePill = l.score != null
    ? `<span class="score-pill ${priority}">${l.score}/10</span>`
    : '<span class="score-pill">—</span>';
  return `
    <div class="lead">
      <div class="lead-header">
        <span class="lead-company">${escapeHtml(l.companyName)}</span>
        ${scorePill}
      </div>
      <div class="lead-meta">
        ${escapeHtml(l.contactName)} · ${escapeHtml(l.industry || 'unknown industry')}
        · <span class="stage-pill">${escapeHtml(l.stage)}</span>
      </div>
      <div class="lead-notes">${escapeHtml(l.crmNotes).slice(0, 200)}${l.crmNotes && l.crmNotes.length > 200 ? '…' : ''}</div>
      <div class="lead-actions">
        <button class="run-workflow-btn" data-lead-id="${l.id}">Run workflow</button>
      </div>
    </div>`;
}

async function runWorkflow(leadId, button) {
  button.disabled = true;
  button.textContent = 'starting…';
  try {
    const r = await api(`/api/leads/${leadId}/run`, { method: 'POST' });
    button.textContent = 'started';
    setTimeout(() => { button.disabled = false; button.textContent = 'Run workflow'; }, 2000);
    startPolling();
    refreshAll();
  } catch (e) {
    button.disabled = false;
    button.textContent = 'Run workflow';
    alert('Failed to start: ' + e.message);
  }
}

// ---------- Runs ----------
async function loadRuns() {
  State.runs = await api('/api/runs?take=20');
  $('#run-count').textContent = State.runs.length;
  $('#runs-list').innerHTML = State.runs.map(renderRun).join('') ||
    '<div class="empty">no runs yet — click "Run workflow" on a lead</div>';
  for (const r of State.runs) {
    if (State.expandedRuns.has(r.id)) {
      loadTraces(r.id);
    }
  }
  $$('.run').forEach((el) => {
    el.addEventListener('click', () => toggleRun(el.dataset.runId));
  });
  // Approval controls live inside the run card — stop clicks from toggling/collapsing it.
  $$('.approval-panel').forEach((p) => p.addEventListener('click', (e) => e.stopPropagation()));
  $$('.approve-btn').forEach((b) => b.addEventListener('click', (e) => {
    e.stopPropagation();
    approveRun(b.dataset.runId);
  }));
  $$('.reject-btn').forEach((b) => b.addEventListener('click', (e) => {
    e.stopPropagation();
    rejectRun(b.dataset.runId);
  }));

  const anyActive = State.runs.some((r) => r.status === 'Pending' || r.status === 'Running');
  if (anyActive) startPolling(); else stopPolling();
}

function renderRun(r) {
  const lead = State.leads.find((l) => l.id === r.leadId);
  const leadName = lead ? lead.companyName : r.leadId.slice(0, 8);
  const status = r.status;
  return `
    <div class="run ${State.expandedRuns.has(r.id) ? 'expanded' : ''}" data-run-id="${r.id}">
      <div class="run-header">
        <span class="run-lead">${escapeHtml(leadName)}</span>
        <span class="run-status ${status.toLowerCase()}">${status}</span>
      </div>
      <div class="run-time muted">${timeAgo(r.startedAt)} · run ${r.id.slice(0, 8)}</div>
      ${r.status === 'AwaitingApproval' ? renderApprovalPanel(r) : ''}
      <div class="run-traces" id="traces-${r.id}">
        <div class="empty">loading…</div>
      </div>
    </div>`;
}

// Inline approve/edit/reject panel, shown on a run paused at AwaitingApproval.
function renderApprovalPanel(r) {
  let draft = {};
  try {
    const pending = JSON.parse(r.pendingStateJson || '{}');
    draft = pending.Draft || pending.draft || {};
  } catch { /* leave draft empty */ }
  const subject = draft.Subject ?? draft.subject ?? '';
  const body = draft.Body ?? draft.body ?? '';
  return `
    <div class="approval-panel" data-run-id="${r.id}">
      <div class="approval-label">Email awaiting approval — edit before sending if needed</div>
      <input id="approve-subject-${r.id}" value="${escapeHtml(subject)}" />
      <textarea id="approve-body-${r.id}">${escapeHtml(body)}</textarea>
      <div class="approval-actions">
        <button class="approve-btn" data-run-id="${r.id}">Approve &amp; send</button>
        <button class="reject-btn" data-run-id="${r.id}">Reject</button>
      </div>
    </div>`;
}

async function toggleRun(runId) {
  if (State.expandedRuns.has(runId)) {
    State.expandedRuns.delete(runId);
    document.querySelector(`.run[data-run-id="${runId}"]`)?.classList.remove('expanded');
  } else {
    State.expandedRuns.add(runId);
    document.querySelector(`.run[data-run-id="${runId}"]`)?.classList.add('expanded');
    await loadTraces(runId);
  }
}

async function loadTraces(runId) {
  try {
    const data = await api(`/api/runs/${runId}`);
    const traces = data.traces || [];
    const container = document.getElementById(`traces-${runId}`);
    if (!container) return;
    if (traces.length === 0) {
      container.innerHTML = '<div class="empty">no traces yet</div>';
      return;
    }
    container.innerHTML = traces.map((t) => `
      <div class="trace">
        <div class="trace-header">
          <span class="trace-agent">${escapeHtml(t.agentName)}</span>
          <span class="trace-status ${t.status}">${t.status} · ${t.durationMs}ms · retries: ${t.retryCount}</span>
        </div>
        ${t.output ? `<div class="trace-detail">${escapeHtml(t.output).slice(0, 800)}</div>` : ''}
        ${t.error ? `<div class="trace-detail" style="color: var(--error)">${escapeHtml(t.error)}</div>` : ''}
      </div>`).join('');
  } catch (e) {
    console.error('loadTraces failed', e);
  }
}

async function approveRun(runId) {
  const subject = document.getElementById(`approve-subject-${runId}`)?.value;
  const body = document.getElementById(`approve-body-${runId}`)?.value;
  try {
    await api(`/api/runs/${runId}/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ subject, body })
    });
    startPolling();
    refreshAll();
  } catch (e) {
    alert('Approve failed: ' + e.message);
  }
}

async function rejectRun(runId) {
  const reason = prompt('Reason for rejecting this email? (optional)');
  if (reason === null) return; // cancelled
  try {
    await api(`/api/runs/${runId}/reject`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ reason })
    });
    refreshAll();
  } catch (e) {
    alert('Reject failed: ' + e.message);
  }
}

// ---------- Outbox + Notifications ----------
async function loadOutbox() {
  State.outbox = await api('/api/outbox?take=20');
  $('#outbox-list').innerHTML = State.outbox.map((o) => `
    <div class="outbox-item">
      <div class="outbox-subject">${escapeHtml(o.subject)}</div>
      <div class="outbox-meta">
        to ${escapeHtml(o.toAddress)} · ${timeAgo(o.generatedAt)}
        · <span class="muted">${escapeHtml(o.filePath)}</span>
      </div>
      <div class="outbox-body">${escapeHtml(o.body)}</div>
    </div>`).join('') || '<div class="empty">no emails sent yet</div>';
}

async function loadNotifications() {
  State.notifications = await api('/api/notifications?take=50');
  $('#notifications-list').innerHTML = State.notifications.map((n) => `
    <div class="notification ${n.severity}">
      <div class="notification-time">${timeAgo(n.timestamp)} · ${escapeHtml(n.channel)}</div>
      <div class="notification-message">${escapeHtml(n.message)}</div>
    </div>`).join('') || '<div class="empty">no notifications yet</div>';
}

// ---------- Tabs ----------
function setupTabs() {
  $$('.tab').forEach((tab) => tab.addEventListener('click', () => {
    $$('.tab').forEach((t) => t.classList.remove('active'));
    $$('.tab-content').forEach((c) => c.classList.add('hidden'));
    tab.classList.add('active');
    document.getElementById(`${tab.dataset.tab}-tab`).classList.remove('hidden');
  }));
}

// ---------- Polling ----------
function startPolling() {
  if (State.pollTimer) return;
  State.pollTimer = setInterval(refreshActive, 2000);
}
function stopPolling() {
  if (State.pollTimer) { clearInterval(State.pollTimer); State.pollTimer = null; }
}
async function refreshActive() {
  await loadRuns();
  await loadOutbox();
  await loadNotifications();
}

async function refreshAll() {
  await Promise.all([loadHealth(), loadLeads(), loadRuns(), loadOutbox(), loadNotifications()]);
}

$('#refresh-runs').addEventListener('click', refreshAll);
setupTabs();
refreshAll();
