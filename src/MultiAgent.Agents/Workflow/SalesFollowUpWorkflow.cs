using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Agents.Agents;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Workflow;

/// <summary>
/// The end-to-end Sales Ops Assistant pipeline. Each agent invocation goes through
/// <see cref="Runner.TracingAgentRunner"/> for retry + tracing; this class only orchestrates
/// the sequential flow, conditional branching, and side-effects (CRM/email/notes).
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
            var scoreJson = JsonSerializer.Serialize(score, AgentJson.Options);

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
                    errorMessage: null, finalScoreJson: scoreJson, finalDraftJson: null, ct);
                return;
            }

            // 2. Research (workflow pre-fetches company info + CRM history and passes to agent)
            var companyInfo = await companyResearch.GetCompanyAsync(lead.Website, ct);
            var crmHistory = await crm.GetCrmHistoryAsync(lead.Id, ct);
            var research = await researcher.InvokeAsync(lead, companyInfo, crmHistory, runId, ct);

            // 3. Outreach draft
            var draft = await outreach.InvokeAsync(lead, score, research, runId, ct);
            var draftJson = JsonSerializer.Serialize(draft, AgentJson.Options);

            // 4. Send email (file outbox)
            var outboxItem = await emailSender.SendAsync(
                runId, lead.Id, lead.ContactEmail, draft.Subject, draft.Body, ct);

            // 5. CRM update — in tool mode (OpenAI) the agent calls the CRM tools directly;
            // otherwise it returns structured instructions the workflow applies.
            // ApplyCrmInstructionsAsync skips any write a tool already did (with a fallback).
            var crmResult = await crmUpdater.InvokeAsync(
                lead, score, research, draft, emailSent: true, runId, crm, ct);
            await ApplyCrmInstructionsAsync(lead.Id, crmResult, ct);

            await notifications.PostAsync(runId, NotificationSeverity.Info,
                $"Lead '{lead.CompanyName}' contacted (score {score.Score}/10). Subject: \"{draft.Subject}\". " +
                $"Email written to {outboxItem.FilePath}.", ct);

            await runStore.SetStatusAsync(runId, RunStatus.Completed,
                errorMessage: null, finalScoreJson: scoreJson, finalDraftJson: draftJson, ct);

            logger.LogInformation("Workflow run {RunId} completed for lead {LeadId}.", runId, leadId);
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

    private async Task ApplyCrmInstructionsAsync(Guid leadId, CrmUpdateResult result, CancellationToken ct)
    {
        var instructions = result.Instructions;

        // In tool mode the agent already performed the stage/CRM-note writes via tools; apply
        // only what a tool did NOT do (deterministic fallback). The Markdown note is never a
        // tool, so it is always written here from the returned instructions.
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
