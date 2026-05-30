namespace MultiAgent.Agents.Runner;

public sealed class AgentResponseParseException(string agentName, string rawResponse, Exception inner)
    : Exception($"Agent '{agentName}' returned a response that could not be deserialized as the expected output type.", inner)
{
    public string AgentName { get; } = agentName;
    public string RawResponse { get; } = rawResponse;
}
