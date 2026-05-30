namespace MultiAgent.Infrastructure.Options;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.Mock;
    public string Model { get; set; } = "gpt-4o-mini";
    public float Temperature { get; set; } = 0.3f;
    public int MaxTokens { get; set; } = 1500;

    /// <summary>For deterministic mock failure-injection in tests. e.g. "OutreachAgent".</summary>
    public string? MockThrowOnAgent { get; set; }
}

public enum LlmProvider
{
    Mock,
    OpenAI
}

public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
}
