using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiAgent.Agents;

internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };
}
