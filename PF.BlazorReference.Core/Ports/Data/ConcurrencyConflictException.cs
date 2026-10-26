namespace PF.BlazorReference.Core.Ports.Data;

/// <summary>
/// Raised by the data port when a save is based on a stale read: another context (another request, or
/// another app host) changed the row first. Infrastructure translates the provider failure into this,
/// so Core never inspects a provider exception.
/// </summary>
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("The row was changed by someone else since it was read.", inner);
