namespace PF.BlazorReference.Web.BackgroundServices;

/// <summary>Who this worker is, for row claims: unique per host and process, and readable in the table.</summary>
public static class ClaimOwner
{
    public static string Current { get; } = $"{Environment.MachineName}:{Environment.ProcessId}";
}
