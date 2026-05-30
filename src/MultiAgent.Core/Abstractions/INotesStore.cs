namespace MultiAgent.Core.Abstractions;

/// <summary>
/// Save a per-lead note as a Markdown file.
/// </summary>
public interface INotesStore
{
    Task WriteLeadNoteAsync(Guid leadId, string markdown, CancellationToken ct);
}
