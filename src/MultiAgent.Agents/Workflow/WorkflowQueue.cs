using System.Threading.Channels;

namespace MultiAgent.Agents.Workflow;

/// <summary>What the background worker should do with a run.</summary>
public enum WorkItemKind
{
    /// <summary>Run the pipeline from the start (qualify → research → draft → send or pause).</summary>
    Start,

    /// <summary>Resume an approved run from the send step (Part B).</summary>
    Resume
}

/// <summary>A unit of background work: which run, its lead, and what to do.</summary>
public readonly record struct WorkItem(Guid RunId, Guid LeadId, WorkItemKind Kind);

/// <summary>
/// In-process dispatch queue between <see cref="WorkflowRunner"/> (producer) and
/// <see cref="WorkflowWorker"/> (consumers). The queue itself is not durable — durability comes from
/// the persisted <c>WorkflowRun</c> rows plus <see cref="WorkflowRecovery"/> re-queuing them on
/// startup, so a process restart cannot strand or lose work.
/// </summary>
public sealed class WorkflowQueue
{
    private readonly Channel<WorkItem> _channel = Channel.CreateUnbounded<WorkItem>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public void Enqueue(WorkItem item) => _channel.Writer.TryWrite(item);

    public ChannelReader<WorkItem> Reader => _channel.Reader;
}
