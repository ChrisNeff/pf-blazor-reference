namespace PF.BlazorReference.Core.Ports.Data;

/// <summary>
/// Raised by the data port when a unique index rejects a write. Infrastructure translates the
/// provider-specific failure into this, so Core never inspects a provider exception.
/// </summary>
public sealed class UniqueConstraintViolationException(Exception inner)
    : Exception("A unique constraint was violated.", inner);
