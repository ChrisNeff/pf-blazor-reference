namespace PF.BlazorReference.Core.Ports.Directory;

/// <summary>
/// Raised by the user-directory port when the directory rejects or fails a request. Infrastructure
/// translates the SDK's failures into this, so Core never inspects an SDK exception. The message is
/// safe to store and show.
/// </summary>
public sealed class UserManagementException(string message, Exception? inner = null)
    : Exception(message, inner);
