namespace PF.BlazorReference.Core.Entities;

/// <summary>
/// Entities stamped by the audit interceptor in Infrastructure. The setters are private so the
/// domain cannot set them; the interceptor writes through the EF change tracker. Both values are
/// <c>default</c> until the first save.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; }
    DateTime LastUpdatedUtc { get; }
}
