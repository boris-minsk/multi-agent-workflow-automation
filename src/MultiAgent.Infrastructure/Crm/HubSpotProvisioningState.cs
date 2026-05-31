namespace MultiAgent.Infrastructure.Crm;

/// <summary>
/// Process-wide cache of whether the HubSpot custom properties exist (and could be created).
/// Registered as a singleton so the existence probe runs at most once per process rather than
/// per request. <c>null</c> means "not yet checked".
/// </summary>
public sealed class HubSpotProvisioningState
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public bool? CustomPropertiesAvailable { get; set; }
}
