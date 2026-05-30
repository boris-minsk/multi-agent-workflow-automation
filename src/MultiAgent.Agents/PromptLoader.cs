using System.Reflection;

namespace MultiAgent.Agents;

internal static class PromptLoader
{
    public static string Load(string name)
    {
        var assembly = typeof(PromptLoader).Assembly;
        var resourceName = $"MultiAgent.Agents.Prompts.{name}.system.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded prompt resource '{resourceName}' not found. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
