using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Ports.Data;

namespace PF.BlazorReference.Core.Features.Invitations;

/// <summary>
/// The ids of the Invited registrations, oldest change first. The acceptance check worker runs
/// <see cref="ActivateAcceptedGuestUseCase"/> on each, so one failing row cannot stop the rest. Claimed
/// rows are listed too; the claim is checked when a row is worked.
/// </summary>
public sealed class ListInvitedRegistrationsUseCase(IConsentIdentityDbFactory dbFactory)
    : IUseCase<ListInvitedRegistrationsUseCase.Request, ErrorOr<ListInvitedRegistrationsUseCase.Response>>
{
    public sealed record Request;

    public sealed record Response(IReadOnlyList<Guid> RegistrationIds);

    public async Task<ErrorOr<ListInvitedRegistrationsUseCase.Response>> Handle(
        ListInvitedRegistrationsUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var ids = await db.DeveloperRegistrations
            .AsNoTracking()
            .Where(r => r.Status == RegistrationStatus.Invited)
            .OrderBy(r => r.LastUpdatedUtc)
            .Select(r => r.Id)
            .ToListAsync(ct);

        return new ListInvitedRegistrationsUseCase.Response(ids);
    }
}
