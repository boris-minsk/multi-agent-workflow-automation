using MultiAgent.Core.Models;

namespace MultiAgent.Agents.Agents;

/// <summary>
/// Result of the CRM update step: the structured <see cref="CrmUpdateInstructions"/> plus which
/// writes (if any) the agent already performed via tools. The workflow uses the flags to apply
/// only the remaining writes — skipping double-writes in tool mode while keeping a deterministic
/// fallback for anything the model returned but did not actually call.
/// </summary>
public sealed record CrmUpdateResult(
    CrmUpdateInstructions Instructions,
    bool StageWrittenByTool,
    bool NoteWrittenByTool);
