namespace MultiAgent.Agents;

/// <summary>
/// Whether CRM function-calling tools are enabled for agents. Tool-calling requires a real
/// model, so this is true only under <c>Llm:Provider=OpenAI</c>; the mock client returns
/// final responses directly and cannot call tools. Registered in
/// <see cref="AgentsServiceCollectionExtensions.AddAgents"/> from configuration.
/// </summary>
public sealed class CrmToolPolicy(bool enabled)
{
    public bool Enabled { get; } = enabled;
}
