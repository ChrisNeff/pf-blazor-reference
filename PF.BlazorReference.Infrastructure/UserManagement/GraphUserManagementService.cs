using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using PF.BlazorReference.Core;
using PF.BlazorReference.Core.Entities;

namespace PF.BlazorReference.Infrastructure.UserManagement;

/// <summary>
/// The user-directory port over Microsoft Graph, as B2B guest invitations in a workforce tenant. The only
/// type that touches the Graph SDK or Azure.Identity (an architecture test enforces it): it builds the
/// <see cref="GraphServiceClient"/> itself and never exposes it, and it translates every SDK failure into
/// <see cref="UserManagementException"/>.
/// <para>
/// The guest account is enabled from the start, because redeeming an invitation is a sign-in and a
/// disabled account cannot sign in. It is unusable until activated because it has no access: activation
/// adds it to the activated group, which the apps (or a Conditional Access policy) require.
/// </para>
/// The app's identity needs the Graph application permissions User.Invite.All, User.Read.All and
/// GroupMember.ReadWrite.All.
/// </summary>
public sealed class GraphUserManagementService : IUserManagementService
{
    private const string AcceptedState = "Accepted";
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    private readonly GraphServiceClient client;
    private readonly GraphUserManagementOptions options;
    private readonly ILogger<GraphUserManagementService> logger;

    private GraphUserManagementService(GraphServiceClient client, GraphUserManagementOptions options, ILogger<GraphUserManagementService> logger)
    {
        this.client = client;
        this.options = options;
        this.logger = logger;
    }

    public static GraphUserManagementService Create(GraphUserManagementOptions options, ILogger<GraphUserManagementService> logger)
    {
        // Managed identity in Azure; the developer's Azure CLI or Visual Studio sign-in locally.
        TokenCredential credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId = options.TenantId,
            ManagedIdentityClientId = options.ManagedIdentityClientId,
        });

        return new GraphUserManagementService(new GraphServiceClient(credential, Scopes), options, logger);
    }

    public Task<string> InviteGuestAsync(DeveloperRegistration registration, CancellationToken ct) => Translate(async () =>
    {
        // A retry after a crash (invitation sent, row not yet saved) finds the guest instead of
        // sending a second invitation email.
        var existing = await client.Users.GetAsync(r =>
        {
            r.QueryParameters.Filter = GuestByEmailFilter(registration.Email);
            r.QueryParameters.Select = ["id"];
            r.QueryParameters.Top = 1;
        }, ct);
        if (existing?.Value?.FirstOrDefault()?.Id is { } existingId)
        {
            logger.LogInformation("A guest for registration {RegistrationId} already exists; reusing it.", registration.Id);
            return existingId;
        }

        if (options.DryRun)
        {
            // Everything up to the write has run: the token, and a read of the directory. A failure,
            // not a success, so the row never claims an invitation that was not sent.
            logger.LogWarning("Dry run: would invite registration {RegistrationId} as a guest.", registration.Id);
            throw new UserManagementException("Dry run: the invitation was not sent (Entra:DryRun is on).");
        }

        var invitation = await client.Invitations.PostAsync(NewInvitation(registration, options.InviteRedirectUrl), cancellationToken: ct);
        return invitation?.InvitedUser?.Id
            ?? throw new UserManagementException("Microsoft Graph returned no guest id for the invitation.");
    });

    public Task<bool> HasAcceptedInvitationAsync(string entraObjectId, CancellationToken ct) => Translate(async () =>
    {
        var user = await client.Users[entraObjectId].GetAsync(r => r.QueryParameters.Select = ["externalUserState"], ct);
        return string.Equals(user?.ExternalUserState, AcceptedState, StringComparison.OrdinalIgnoreCase);
    });

    public Task ActivateGuestAsync(string entraObjectId, CancellationToken ct) => Translate(async () =>
    {
        if (options.DryRun)
        {
            logger.LogWarning("Dry run: would add guest {ObjectId} to the activated group.", entraObjectId);
            throw new UserManagementException("Dry run: the guest was not activated (Entra:DryRun is on).");
        }

        try
        {
            await client.Groups[options.ActivatedGroupId].Members.Ref.PostAsync(
                new ReferenceCreate { OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{entraObjectId}" },
                cancellationToken: ct);
        }
        catch (ODataError ex) when (IsAlreadyAMember(ex))
        {
            // Activated before, and the row was not saved. Nothing to do.
        }
        return true;
    });

    internal static Invitation NewInvitation(DeveloperRegistration registration, string inviteRedirectUrl) => new()
    {
        InvitedUserEmailAddress = registration.Email,
        InvitedUserDisplayName = registration.Name,
        InvitedUserType = "Guest",
        InviteRedirectUrl = inviteRedirectUrl,
        // Entra sends the email; redeeming it is where the invitee proves the address (by a one-time
        // code, or by signing in with an account that already owns it).
        SendInvitationMessage = true,
    };

    /// <summary>An OData filter for the guest with this email. Quotes are doubled, as OData requires.</summary>
    internal static string GuestByEmailFilter(string email) =>
        $"mail eq '{email.Replace("'", "''", StringComparison.Ordinal)}' and userType eq 'Guest'";

    // Graph answers a duplicate member reference with 400 and this message; there is no distinct code.
    private static bool IsAlreadyAMember(ODataError ex) =>
        ex.ResponseStatusCode == 400
        && ex.Error?.Message?.Contains("already exist", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<T> Translate<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ODataError ex)
        {
            throw new UserManagementException(
                $"Microsoft Graph refused the request ({ex.ResponseStatusCode} {ex.Error?.Code}): {ex.Error?.Message}", ex);
        }
        catch (ApiException ex)
        {
            throw new UserManagementException($"Microsoft Graph failed the request ({ex.ResponseStatusCode}).", ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new UserManagementException("The app could not authenticate to Microsoft Graph.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UserManagementException("Microsoft Graph could not be reached.", ex);
        }
    }
}
