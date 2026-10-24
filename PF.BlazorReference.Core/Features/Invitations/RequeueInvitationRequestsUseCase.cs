using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Features.Invitations;

/// <summary>
/// Puts every requested invitation back on the queue. The invitation worker runs it at startup and on
/// its sweep interval, which recovers requests whose signal was lost: a restart, a full queue, or a
/// request made on another host. An id already queued may be queued twice; the second
/// <see cref="SendInvitationUseCase"/> finds the row no longer requested and skips it.
/// </summary>
public sealed class RequeueInvitationRequestsUseCase(IConsentIdentityDbFactory dbFactory, IInvitationQueue queue)
    : IUseCase<RequeueInvitationRequestsUseCase.Request, ErrorOr<RequeueInvitationRequestsUseCase.Response>>
{
    public sealed record Request;

    /// <param name="Refused">Requests the full queue refused; they wait for the next sweep.</param>
    public sealed record Response(int Queued, int Refused);

    public async Task<ErrorOr<RequeueInvitationRequestsUseCase.Response>> Handle(
        RequeueInvitationRequestsUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var ids = await db.DeveloperRegistrations
            .AsNoTracking()
            .Where(r => r.Status == RegistrationStatus.ProvisioningRequested)
            .OrderBy(r => r.LastUpdatedUtc)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var queued = ids.Count(queue.TryEnqueue);
        return new RequeueInvitationRequestsUseCase.Response(queued, ids.Count - queued);
    }
}
