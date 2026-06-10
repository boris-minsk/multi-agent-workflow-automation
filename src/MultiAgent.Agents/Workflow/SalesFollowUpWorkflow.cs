using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Agents.Agents;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

/// <summary>
/// The end-to-end Sales Ops Assistant pipeline. Each agent invocation goes through
/// <see cref="Runner.TracingAgentRunner"/> for retry + tracing; this class orchestrates the
/// sequential flow, conditional branching, the human-approval pause, and side-effects
/// (CRM/email/notes).
/// </summary>
public sealed class SalesFollowUpWorkflow(
    ICrmRepository crm,
    IEmailSender emailSender,
    ICompanyResearchSource companyResearch,
    INotesStore notes,
    INotificationSink notifications,
    IWorkflowRunStore runStore,
    LeadQualificationAgent qualifier,
    ResearchAgent researcher,
    OutreachAgent outreach,
    CrmUpdateAgent crmUpdater,
    IOptions<WorkflowOptions> workflowOptions,
    ILogger<SalesFollowUpWorkflow> logger)
{
    private readonly WorkflowOptions _options = workflowOptions.Value;

    /// <summary>A lead is "high value" (worth a reviewer's time) at High priority or score &gt;= 8.</summary>
    private const int HighValueScoreThreshold = 8;

    /// <summary>
    /// Part A of the pipeline: qualify, (skip on low score), research, draft. Then either pause for
    /// human approval (status AwaitingApproval) or, when approval is not required, run Part B inline.
    /// </summary>
    public async Task RunAsync(Guid runId, Guid leadId, CancellationToken ct)
    {
        logger.LogInformation("Workflow run {RunId} starting for lead {LeadId}.", runId, leadId);

        try
        {
            await runStore.SetStatusAsync(runId, RunStatus.Running, null, null, null, ct);

            var lead = await crm.GetAsync(leadId, ct)
                ?? throw new InvalidOperationException($"Lead {leadId} not found");

            // 1. Qualification
            var score = await qualifier.InvokeAsync(lead, runId, ct);
            await crm.UpdateScoreAsync(lead.Id, score.Score, score.Priority, score.Reason, ct);

            if (score.Score < _options.QualificationThreshold)
            {
                logger.LogInformation(
                    "Lead {LeadId} scored {Score}/10 < threshold {Threshold}; skipping outreach.",
                    leadId, score.Score, _options.QualificationThreshold);

                var skipResult = await crmUpdater.InvokeAsync(
                    lead, score, research: null, draft: null, emailSent: false, runId, crm, ct);

                await ApplyCrmInstructionsAsync(lead.Id, skipResult, ct);

                await notifications.PostAsync(runId, NotificationSeverity.Info,
                    $"Lead '{lead.CompanyName}' marked {skipResult.Instructions.TargetStage} (score {score.Score}/10).", ct);

                await runStore.SetStatusAsync(runId, RunStatus.Skipped,
                    errorMessage: null,
                    finalScoreJson: JsonSerializer.Serialize(score, AgentJson.Options),
                    finalDraftJson: null, ct);
                return;
            }

            // 2. Research (workflow pre-fetches company info + CRM history and passes to agent)
            var companyInfo = await companyResearch.GetCompanyAsync(lead.Website, ct);
            var crmHistory = await crm.GetCrmHistoryAsync(lead.Id, ct);
            var research = await researcher.InvokeAsync(lead, companyInfo, crmHistory, runId, ct);

            // 3. Outreach draft
            var draft = await outreach.InvokeAsync(lead, score, research, runId, ct);

            // 4. Human-in-the-loop gate. When approval is required, park the run with the draft +
            // resume state; a reviewer approves/rejects via the API, which calls ResumeAfterApprovalAsync
            // or marks the run Rejected. Otherwise send immediately (Part B).
            if (RequiresApproval(score))
            {
                var pending = new PendingApprovalState { Score = score, Research = research, Draft = draft };
                await runStore.SetAwaitingApprovalAsync(
                    runId, JsonSerializer.Serialize(pending, AgentJson.Options), ct);

                await notifications.PostAsync(runId, NotificationSeverity.Warning,
                    $"Lead '{lead.CompanyName}' is waiting for email approval (score {score.Score}/10). " +
                    $"Subject: \"{draft.Subject}\".", ct);

                logger.LogInformation("Workflow run {RunId} awaiting approval for lead {LeadId}.", runId, leadId);
                return;
            }

            await CompleteWithSendAsync(lead, score, research, draft, runId, ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Workflow run {RunId} canceled.", runId);
            await runStore.SetStatusAsync(runId, RunStatus.Failed, "Canceled", null, null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow run {RunId} failed for lead {LeadId}.", runId, leadId);
            await runStore.SetStatusAsync(runId, RunStatus.Failed, ex.Message, null, null, CancellationToken.None);
            await notifications.PostAsync(runId, NotificationSeverity.Error,
                $"Workflow run failed for lead {leadId}: {ex.Message}", CancellationToken.None);
        }
    }

    /// <summary>
    /// Resumes a run a reviewer approved: rehydrates the saved score/research/draft and runs Part B
    /// (send → CRM update → complete). The status was already moved to Running by the caller's atomic
    /// approval transition, so this owns failure handling from here (same shape as <see cref="RunAsync"/>).
    /// </summary>
    public async Task ResumeAfterApprovalAsync(Guid runId, Guid leadId, CancellationToken ct)
    {
        logger.LogInformation("Workflow run {RunId} resuming after approval for lead {LeadId}.", runId, leadId);

        try
        {
            var run = await runStore.GetAsync(runId, ct)
                ?? throw new InvalidOperationException($"WorkflowRun {runId} not found");
            if (string.IsNullOrWhiteSpace(run.PendingStateJson))
            {
                throw new InvalidOperationException($"Run {runId} has no pending approval state to resume.");
            }

            var pending = JsonSerializer.Deserialize<PendingApprovalState>(run.PendingStateJson, AgentJson.Options)
                ?? throw new InvalidOperationException($"Run {runId} pending approval state could not be parsed.");

            var lead = await crm.GetAsync(leadId, ct)
                ?? throw new InvalidOperationException($"Lead {leadId} not found");

            await CompleteWithSendAsync(lead, pending.Score, pending.Research, pending.Draft, runId, ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Workflow run {RunId} canceled during resume.", runId);
            await runStore.SetStatusAsync(runId, RunStatus.Failed, "Canceled", null, null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow run {RunId} failed during resume for lead {LeadId}.", runId, leadId);
            await runStore.SetStatusAsync(runId, RunStatus.Failed, ex.Message, null, null, CancellationToken.None);
            await notifications.PostAsync(runId, NotificationSeverity.Error,
                $"Workflow run failed after approval for lead {leadId}: {ex.Message}", CancellationToken.None);
        }
    }

    /// <summary>
    /// Records a reviewer's rejection: no email is sent. Writes a CRM note + an Info notification and
    /// intentionally leaves the lead's stage unchanged — a human rejecting one draft does not
    /// disqualify a qualified lead (unlike the low-score skip path, which sets Disqualified). The run's
    /// status was already moved to Rejected by the caller's atomic transition.
    /// </summary>
    public async Task ApplyRejectionAsync(Guid leadId, Guid runId, string? reason, CancellationToken ct)
    {
        var note = string.IsNullOrWhiteSpace(reason)
            ? "Outreach email rejected by reviewer; not sent."
            : $"Outreach email rejected by reviewer: {reason}";

        await crm.AddNoteAsync(leadId, note, ct);
        await notifications.PostAsync(runId, NotificationSeverity.Info,
            $"Outreach for lead {leadId} was rejected by the reviewer; no email sent.", ct);

        logger.LogInformation("Workflow run {RunId} rejected for lead {LeadId}.", runId, leadId);
    }

    /// <summary>
    /// Part B of the pipeline: send the (approved) draft, run the CRM update, notify, and mark the run
    /// Completed. Shared by the no-approval path and the post-approval resume; the draft passed in is
    /// the one that gets sent and recorded (so reviewer edits are honored).
    /// </summary>
    private async Task CompleteWithSendAsync(
        Lead lead, LeadScore score, ResearchSummary research, OutreachDraft draft, Guid runId, CancellationToken ct)
    {
        // Send email (file outbox or SMTP, per Email:Provider)
        var outboxItem = await emailSender.SendAsync(
            runId, lead.Id, lead.ContactEmail, draft.Subject, draft.Body, ct);

        // CRM update — in tool mode (OpenAI) the agent calls the CRM tools directly; otherwise it
        // returns structured instructions the workflow applies. ApplyCrmInstructionsAsync skips any
        // write a tool already did (with a deterministic fallback).
        var crmResult = await crmUpdater.InvokeAsync(
            lead, score, research, draft, emailSent: true, runId, crm, ct);
        await ApplyCrmInstructionsAsync(lead.Id, crmResult, ct);

        await notifications.PostAsync(runId, NotificationSeverity.Info,
            $"Lead '{lead.CompanyName}' contacted (score {score.Score}/10). Subject: \"{draft.Subject}\". " +
            $"Email written to {outboxItem.FilePath}.", ct);

        await runStore.SetStatusAsync(runId, RunStatus.Completed,
            errorMessage: null,
            finalScoreJson: JsonSerializer.Serialize(score, AgentJson.Options),
            finalDraftJson: JsonSerializer.Serialize(draft, AgentJson.Options), ct);

        logger.LogInformation("Workflow run {RunId} completed for lead {LeadId}.", runId, lead.Id);
    }

    private bool RequiresApproval(LeadScore score) => _options.ApprovalMode switch
    {
        ApprovalMode.Always => true,
        ApprovalMode.HighValueOnly => score.Priority == Priority.High || score.Score >= HighValueScoreThreshold,
        _ => false
    };

    private async Task ApplyCrmInstructionsAsync(Guid leadId, CrmUpdateResult result, CancellationToken ct)
    {
        var instructions = result.Instructions;

        // In tool mode the agent already performed the stage/CRM-note writes via tools; apply only
        // what a tool did NOT do (deterministic fallback). The Markdown note is never a tool, so it
        // is always written here from the returned instructions.
        if (!result.StageWrittenByTool)
        {
            await crm.UpdateStageAsync(leadId, instructions.TargetStage, ct);
        }
        if (!result.NoteWrittenByTool && !string.IsNullOrWhiteSpace(instructions.ShortNote))
        {
            await crm.AddNoteAsync(leadId, instructions.ShortNote, ct);
        }
        if (!string.IsNullOrWhiteSpace(instructions.MarkdownNote))
        {
            await notes.WriteLeadNoteAsync(leadId, instructions.MarkdownNote, ct);
        }
    }
}
