namespace PF.BlazorReference.Core;

/// <summary>
/// Wakes the background worker that sends Entra invitations. A signal, not the record of the work:
/// the registration row's status is. A lost or refused signal only delays the work until the
/// worker's next sweep of the table, so enqueueing never blocks and never fails the caller.
/// </summary>
public interface IProvisioningQueue
{
    /// <returns>False when the queue is full; the sweep picks the registration up later.</returns>
    bool TryEnqueue(Guid registrationId);
}
