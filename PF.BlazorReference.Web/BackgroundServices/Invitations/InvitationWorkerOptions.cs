namespace PF.BlazorReference.Web.BackgroundServices.Invitations;

/// <summary>Bound from "BackgroundServices:Invitations".</summary>
public sealed class InvitationWorkerOptions
{
    public const string SectionName = "BackgroundServices:Invitations";

    /// <summary>How often requested invitations are put back on the queue, to recover lost signals. Also at startup.</summary>
    public int SweepIntervalSeconds { get; set; } = 300;
}
