using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Extensions;

namespace PF.BlazorReference.Core.Features.Registration;

/// <summary>Lists registrations, newest first. A read use case: no tracking, projected to a view model.</summary>
public sealed class ListRegistrationsUseCase(IConsentIdentityDbFactory dbFactory)
    : IUseCase<ListRegistrationsUseCase.Request, ErrorOr<ListRegistrationsUseCase.Response>>
{
    public sealed record Request;

    public sealed record Response(IReadOnlyList<ListRegistrationsUseCase.Item> Items);

    public sealed record Item(Guid Id, string Name, string Email, RegistrationStatus Status, DateTime CreatedAtUtc);

    public async Task<ErrorOr<ListRegistrationsUseCase.Response>> Handle(
        ListRegistrationsUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var items = await db.RegistrationsNewestFirst()
            .AsNoTracking()
            .Select(r => new ListRegistrationsUseCase.Item(r.Id, r.Name, r.Email, r.Status, r.CreatedAtUtc))
            .ToListAsync(ct);

        return new ListRegistrationsUseCase.Response(items);
    }
}
