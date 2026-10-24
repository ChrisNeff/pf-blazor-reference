namespace PF.BlazorReference.Web.BackgroundServices.AcceptanceCheck;

/// <summary>Bound from "BackgroundServices:AcceptanceCheck".</summary>
public sealed class AcceptanceCheckOptions
{
    public const string SectionName = "BackgroundServices:AcceptanceCheck";

    /// <summary>How often Entra is asked whether invited guests have redeemed. Also at startup.</summary>
    public int IntervalSeconds { get; set; } = 300;
}
