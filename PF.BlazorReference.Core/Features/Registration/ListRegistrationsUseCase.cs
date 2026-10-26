using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;
using PF.BlazorReference.Core.Ports.Data;

namespace PF.BlazorReference.Core.Features.Registration;

/// <summary>
/// Lists registrations, newest first. A read use case: no tracking, projected to its own contract.
/// The contract never exposes an entity type (not even <see cref="RegistrationStatus"/>), so the web
/// is not coupled to the domain model; <see cref="ListRegistrationsUseCase.Status"/> is mapped here.
/// </summary>
public sealed class ListRegistrationsUseCase(IConsentIdentityDbFactory dbFactory)
    : IUseCase<ListRegistrationsUseCase.Request, ErrorOr<ListRegistrationsUseCase.Response>>
{
    public sealed record Request;

    public sealed record Response(IReadOnlyList<ListRegistrationsUseCase.Item> Items);

    /// <summary>Where a registration is in the invitation flow, as callers see it.</summary>
    public enum Status
    {
        /// <summary>The default value: never returned.</summary>
        Invalid = 0,
        Submitted,
        InvitationRequested,
        Invited,
        Activated,
        InvitationFailed,
    }

    /// <param name="CanRequestInvitation">The domain's answer to whether an invitation can be requested now.</param>
    /// <param name="ProvisioningError">Why the last Entra call failed, if it did.</param>
    public sealed record Item(
        Guid Id,
        string Name,
        string Email,
        ListRegistrationsUseCase.Status Status,
        DateTime CreatedAtUtc,
        bool CanRequestInvitation,
        string? ProvisioningError);

    public async Task<ErrorOr<ListRegistrationsUseCase.Response>> Handle(
        ListRegistrationsUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var rows = await db.RegistrationsNewestFirst()
            .AsNoTracking()
            .Select(r => new { r.Id, r.Name, r.Email, r.Status, r.CreatedAtUtc, r.ProvisioningError })
            .ToListAsync(ct);

        // Mapped in memory: the switch is not translatable to SQL, and the rows are already projected.
        var items = rows
            .Select(r => new ListRegistrationsUseCase.Item(
                r.Id, r.Name, r.Email, ToContract(r.Status), r.CreatedAtUtc,
                DeveloperRegistration.CanRequestProvisioning(r.Status), r.ProvisioningError))
            .ToList();

        return new ListRegistrationsUseCase.Response(items);
    }

    private static ListRegistrationsUseCase.Status ToContract(RegistrationStatus status) => status switch
    {
        RegistrationStatus.Submitted => ListRegistrationsUseCase.Status.Submitted,
        RegistrationStatus.ProvisioningRequested => ListRegistrationsUseCase.Status.InvitationRequested,
        RegistrationStatus.Invited => ListRegistrationsUseCase.Status.Invited,
        RegistrationStatus.Activated => ListRegistrationsUseCase.Status.Activated,
        RegistrationStatus.ProvisioningFailed => ListRegistrationsUseCase.Status.InvitationFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a persisted status."),
    };
}
