using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Features.Provisioning;

/// <summary>
/// The registrations the worker still has a step for, oldest first: invitations to send
/// (ProvisioningRequested) and invitations to check for redemption (Invited). The worker sweeps these
/// at startup and periodically, which also recovers requests whose queue signal was lost.
/// </summary>
public sealed class ListProvisioningBacklogUseCase(IConsentIdentityDbFactory dbFactory)
    : IUseCase<ListProvisioningBacklogUseCase.Request, ErrorOr<ListProvisioningBacklogUseCase.Response>>
{
    public sealed record Request;

    public sealed record Response(IReadOnlyList<Guid> RegistrationIds);

    public async Task<ErrorOr<ListProvisioningBacklogUseCase.Response>> Handle(
        ListProvisioningBacklogUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var ids = await db.DeveloperRegistrations
            .AsNoTracking()
            .Where(r => r.Status == RegistrationStatus.ProvisioningRequested || r.Status == RegistrationStatus.Invited)
            .OrderBy(r => r.LastUpdatedUtc)
            .Select(r => r.Id)
            .ToListAsync(ct);

        return new ListProvisioningBacklogUseCase.Response(ids);
    }
}
