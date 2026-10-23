using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Core.Features.Provisioning;

/// <summary>
/// Asks for an Entra guest invitation for a registration. Records the request on the row first, which
/// is what makes it durable, then wakes the worker. The invitation itself is sent by
/// <see cref="ProvisionDeveloperUseCase"/> in the background.
/// </summary>
public sealed class RequestEntraProvisioningUseCase(IConsentIdentityDbFactory dbFactory, IProvisioningQueue queue)
    : IUseCase<RequestEntraProvisioningUseCase.Request, ErrorOr<RequestEntraProvisioningUseCase.Response>>
{
    public const string RegistrationIdField = nameof(RequestEntraProvisioningUseCase.Request.RegistrationId);

    public sealed record Request(Guid RegistrationId);

    public sealed record Response(RegistrationStatus Status);

    public async Task<ErrorOr<RequestEntraProvisioningUseCase.Response>> Handle(
        RequestEntraProvisioningUseCase.Request request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateAsync(ct);

        var registration = await db.DeveloperRegistrations.FirstOrDefaultAsync(r => r.Id == request.RegistrationId, ct);
        if (registration is null)
            return Error.NotFound(RegistrationIdField, "The registration no longer exists.");

        var requested = registration.RequestProvisioning();
        if (requested.IsError)
            return requested.Errors;

        await db.SaveChangesAsync(ct);

        // After the save, so the worker never reads the row before the request is on it. A full queue
        // is not a failure: the worker's sweep finds the row.
        queue.TryEnqueue(registration.Id);

        return new RequestEntraProvisioningUseCase.Response(registration.Status);
    }
}
