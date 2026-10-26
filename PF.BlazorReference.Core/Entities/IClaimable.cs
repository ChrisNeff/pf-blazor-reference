namespace PF.BlazorReference.Core.Entities;

/// <summary>
/// An entity a worker takes for a lease before it calls an external system, so that when the app runs
/// on several hosts only one of them does the work. Claims only count once saved; implementers are also
/// <see cref="IVersioned"/>, so two hosts that take the same claim race on the version and the second
/// save is rejected. See <c>ClaimableDbExtensions</c>.
/// </summary>
public interface IClaimable : IEntity, IVersioned
{
    /// <summary>The worker (host and process) that holds the row, if any.</summary>
    string? ClaimedBy { get; }

    /// <summary>When the claim lapses, so a row held by a crashed host is picked up again.</summary>
    DateTime? ClaimedUntilUtc { get; }

    /// <summary>Takes the row for <paramref name="owner"/>. Refused while another owner's claim is still live.</summary>
    bool TryClaim(string owner, DateTime nowUtc, TimeSpan lease);

    /// <summary>Gives the row back.</summary>
    void ReleaseClaim();
}
