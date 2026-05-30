using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiAgent.Agents.Workflow;
using MultiAgent.Core.Abstractions;
using MultiAgent.Core.Models;
using Polly;
using Polly.Retry;

namespace MultiAgent.Agents.Runner;

/// <summary>
/// Decorates <see cref="IAgentRunner"/> with Polly retry, per-attempt tracing, and a
/// terminal-failure notification — the "monitoring/recovery agent" role from the brief.
/// </summary>
public sealed class TracingAgentRunner : IAgentRunner
{
    private readonly IAgentRunner _inner;
    private readonly IAgentTracer _tracer;
    private readonly INotificationSink _notifications;
    private readonly ILogger<TracingAgentRunner> _logger;
    private readonly ResiliencePipeline _pipeline;

    public TracingAgentRunner(
        IAgentRunner inner,
        IAgentTracer tracer,
        INotificationSink notifications,
        IOptions<WorkflowOptions> workflowOptions,
        ILogger<TracingAgentRunner> logger)
    {
        _inner = inner;
        _tracer = tracer;
        _notifications = notifications;
        _logger = logger;

        var opts = workflowOptions.Value;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
                MaxRetryAttempts = opts.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(opts.RetryBaseDelayMs),
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "Retrying agent invocation (attempt {Attempt}) after exception.",
                        args.AttemptNumber + 1);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<TOutput> RunAsync<TOutput>(
        AIAgent agent,
        string agentName,
        string userMessage,
        Guid runId,
        CancellationToken ct) where TOutput : class
    {
        var totalAttempts = 0;
        TOutput? finalResult = null;
        Exception? lastError = null;

        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                totalAttempts++;
                var sw = Stopwatch.StartNew();
                try
                {
                    finalResult = await _inner.RunAsync<TOutput>(agent, agentName, userMessage, runId, token);
                    sw.Stop();
                    await _tracer.RecordAsync(new AgentTrace
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        AgentName = agentName,
                        Step = agentName,
                        Input = Truncate(userMessage, 4000),
                        Output = Truncate(JsonSerializer.Serialize(finalResult, AgentJson.Options), 4000),
                        Status = RunStatus.Completed,
                        DurationMs = sw.ElapsedMilliseconds,
                        RetryCount = totalAttempts - 1,
                        Timestamp = DateTime.UtcNow
                    }, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    sw.Stop();
                    lastError = ex;
                    await _tracer.RecordAsync(new AgentTrace
                    {
                        Id = Guid.NewGuid(),
                        RunId = runId,
                        AgentName = agentName,
                        Step = agentName,
                        Input = Truncate(userMessage, 4000),
                        Output = null,
                        Status = RunStatus.Failed,
                        DurationMs = sw.ElapsedMilliseconds,
                        RetryCount = totalAttempts - 1,
                        Timestamp = DateTime.UtcNow,
                        Error = Truncate(ex.Message, 1000)
                    }, token);
                    throw;
                }
            }, ct);

            return finalResult
                ?? throw new InvalidOperationException($"Agent '{agentName}' returned no value despite no exception.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _notifications.PostAsync(runId, NotificationSeverity.Error,
                $"Agent '{agentName}' failed terminally after {totalAttempts} attempt(s): {ex.Message}", ct);
            throw;
        }
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}
