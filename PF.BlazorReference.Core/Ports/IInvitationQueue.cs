namespace PF.BlazorReference.Core.Ports;

/// <summary>
/// Wakes the invitation worker. A signal, not the record of the work: the registration row's status is.
/// A lost or refused signal only delays the work until the worker's next sweep of the table, so
/// enqueueing never blocks and never fails the caller. Implemented in Infrastructure, in memory and per host.
/// </summary>
public interface IInvitationQueue
{
    /// <returns>False when the queue is full; the sweep picks the registration up later.</returns>
    bool TryEnqueue(Guid registrationId);

    /// <summary>The queued registration ids, as they arrive, until <paramref name="ct"/> is cancelled. One reader only.</summary>
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}
