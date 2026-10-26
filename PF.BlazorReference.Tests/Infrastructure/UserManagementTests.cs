using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PF.BlazorReference.Core;
using PF.BlazorReference.Infrastructure;
using PF.BlazorReference.Infrastructure.UserManagement;
using PF.BlazorReference.Core.Ports.Directory;

namespace PF.BlazorReference.Tests.Infrastructure;

/// <summary>
/// The Graph adapter's pure parts and its registration. Nothing here calls Graph: the requests
/// themselves are checked by hand against a test tenant. The Graph model types stay invisible to this
/// project (PrivateAssets="compile" in Infrastructure), so the built Invitation is not inspected here.
/// </summary>
public class UserManagementTests
{
    [Fact]
    public void The_guest_lookup_filters_on_the_email_and_the_guest_user_type()
    {
        Assert.Equal("mail eq 'ada@example.com' and userType eq 'Guest'",
            GraphUserManagementService.GuestByEmailFilter("ada@example.com"));
    }

    [Fact]
    public void A_quote_in_the_email_is_doubled_so_it_cannot_end_the_filter_string()
    {
        Assert.Equal("mail eq 'o''brien@example.com' and userType eq 'Guest'",
            GraphUserManagementService.GuestByEmailFilter("o'brien@example.com"));
    }

    private static readonly (string, string?)[] GraphSettings =
    [
        ("Entra:InviteRedirectUrl", "https://localhost:5027/"),
        ("Entra:ActivatedGroupId", "00000000-0000-0000-0000-0000000000aa"),
    ];

    private static IServiceProvider Build(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))
                .Append(new("ConnectionStrings:ConsentIdentity", "Data Source=:memory:")))
            .Build();
        return new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
    }

    [Fact]
    public void The_fake_directory_is_used_when_configured()
    {
        var services = Build(("Entra:UseFakeDirectory", "true"));

        Assert.IsType<FakeUserManagementService>(services.GetRequiredService<IUserManagementService>());
    }

    [Fact]
    public void Graph_is_used_when_the_fake_is_off()
    {
        var services = Build(GraphSettings);

        Assert.IsType<GraphUserManagementService>(services.GetRequiredService<IUserManagementService>());
    }

    [Theory]
    [InlineData("Entra:InviteRedirectUrl")]
    [InlineData("Entra:ActivatedGroupId")]
    public void Startup_fails_when_graph_is_used_without_a_required_setting(string missing)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(GraphSettings.Where(s => s.Item1 != missing).ToArray()));

        Assert.Contains(missing, ex.Message);
    }
}
