using Microsoft.Extensions.Logging;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Infrastructure.UserManagement;

/// <summary>
/// Stands in for Entra in Development and in tests (<c>Entra:UseFakeDirectory</c>), so neither needs a
/// tenant. It sends nothing: it logs, returns a guest id derived from the registration, and treats every
/// invitation as already redeemed, so the next sweep activates it.
/// </summary>
public sealed class FakeUserManagementService(ILogger<FakeUserManagementService> logger) : IUserManagementService
{
    public Task<string> InviteGuestAsync(DeveloperRegistration registration, CancellationToken ct)
    {
        logger.LogInformation("Fake directory: pretending to invite registration {RegistrationId}.", registration.Id);
        return Task.FromResult($"fake-{registration.Id:N}");
    }

    public Task<bool> HasAcceptedInvitationAsync(string entraObjectId, CancellationToken ct) => Task.FromResult(true);

    public Task ActivateGuestAsync(string entraObjectId, CancellationToken ct)
    {
        logger.LogInformation("Fake directory: pretending to activate guest {ObjectId}.", entraObjectId);
        return Task.CompletedTask;
    }
}
