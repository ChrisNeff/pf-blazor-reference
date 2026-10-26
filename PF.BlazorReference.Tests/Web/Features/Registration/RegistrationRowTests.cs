using MudBlazor;
using PF.BlazorReference.Core.Features.Registration;
using PF.BlazorReference.Web.Features.Registration.ViewModels;

namespace PF.BlazorReference.Tests.Web.Features.Registration;

/// <summary>
/// The view model is where the table's presentation decisions live, so they are tested here as plain
/// data instead of through rendered markup.
/// </summary>
public class RegistrationRowTests
{
    private static ListRegistrationsUseCase.Item Item(
        ListRegistrationsUseCase.Status status, bool canRequestInvitation = false, string? error = null) =>
        new(Guid.NewGuid(), "Ada", "ada@example.com", status, new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc),
            canRequestInvitation, error);

    [Theory]
    [InlineData(ListRegistrationsUseCase.Status.Submitted, "Submitted", Color.Default)]
    [InlineData(ListRegistrationsUseCase.Status.InvitationRequested, "Sending invite", Color.Info)]
    [InlineData(ListRegistrationsUseCase.Status.Invited, "Invited", Color.Primary)]
    [InlineData(ListRegistrationsUseCase.Status.Activated, "Activated", Color.Success)]
    [InlineData(ListRegistrationsUseCase.Status.InvitationFailed, "Failed", Color.Error)]
    public void Each_status_has_its_label_and_color(ListRegistrationsUseCase.Status status, string label, Color color)
    {
        var row = RegistrationRow.From(Item(status));

        Assert.Equal((label, color), (row.StatusLabel, row.StatusColor));
    }

    [Fact]
    public void Copies_the_identity_and_data_columns()
    {
        var item = Item(ListRegistrationsUseCase.Status.Submitted);

        var row = RegistrationRow.From(item);

        Assert.Equal((item.Id, item.Name, item.Email, item.CreatedAtUtc), (row.Id, row.Name, row.Email, row.CreatedAtUtc));
    }

    [Fact]
    public void Inviting_follows_the_domains_answer_not_the_status()
    {
        Assert.True(RegistrationRow.From(Item(ListRegistrationsUseCase.Status.Submitted, canRequestInvitation: true)).CanInvite);
        Assert.False(RegistrationRow.From(Item(ListRegistrationsUseCase.Status.Submitted, canRequestInvitation: false)).CanInvite);
    }

    [Fact]
    public void A_failed_row_offers_a_retry_and_shows_why_it_failed()
    {
        var row = RegistrationRow.From(Item(ListRegistrationsUseCase.Status.InvitationFailed, canRequestInvitation: true, error: "Graph was down."));

        Assert.Equal("Retry invite", row.InviteLabel);
        Assert.Equal("Graph was down.", row.StatusHint);
    }

    [Fact]
    public void An_invited_row_explains_the_wait_unless_activation_failed()
    {
        Assert.Equal("Invitation sent. Waiting for the invitee to redeem it.",
            RegistrationRow.From(Item(ListRegistrationsUseCase.Status.Invited)).StatusHint);
        Assert.Equal("Group not found.",
            RegistrationRow.From(Item(ListRegistrationsUseCase.Status.Invited, error: "Group not found.")).StatusHint);
    }

    [Theory]
    [InlineData(ListRegistrationsUseCase.Status.Submitted, false)]
    [InlineData(ListRegistrationsUseCase.Status.InvitationRequested, true)]
    [InlineData(ListRegistrationsUseCase.Status.Invited, false)]
    public void Only_a_requested_row_is_being_sent(ListRegistrationsUseCase.Status status, bool sending) =>
        Assert.Equal(sending, RegistrationRow.From(Item(status)).IsSendingInvitation);
}
