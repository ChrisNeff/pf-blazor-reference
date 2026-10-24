using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Features.Invitations;

/// <summary>
/// The ids of the registrations in one status, oldest change first. The acceptance check worker uses it
/// for Invited rows, then runs <see cref="ActivateAcceptedGuestUseCase"/> on each, so one failing row
/// cannot stop the rest. Claimed rows are listed too; the claim is checked when a row is worked.
/// </summary>
public sealed class ListRegistrationIdsUseCase(IConsentIdentityDbFactory dbFactory)
    : IUseCase<ListRegistrationIdsUseCase.Request, ErrorOr<ListRegistrationIdsUseCase.Response>>
{
    public sealed record Request(RegistrationStatus Status);

    public sealed record Response(IReadOnlyList<Guid> RegistrationIds);

    public async Task<ErrorOr<ListRegistrationIdsUseCase.Response>> Handle(
        ListRegistrationIdsUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var ids = await db.DeveloperRegistrations
            .AsNoTracking()
            .Where(r => r.Status == request.Status)
            .OrderBy(r => r.LastUpdatedUtc)
            .Select(r => r.Id)
            .ToListAsync(ct);

        return new ListRegistrationIdsUseCase.Response(ids);
    }
}
