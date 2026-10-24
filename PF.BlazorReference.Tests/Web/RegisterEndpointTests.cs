using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PF.BlazorReference.Core.Entities;
using PF.BlazorReference.Core.Features.Registration;

namespace PF.BlazorReference.Tests.Web;

/// <summary>HTTP-level tests of the static SSR form: antiforgery, redirect-after-post, and error display.</summary>
public sealed class RegisterEndpointTests : IClassFixture<RegisterEndpointTests.AppFactory>
{
    private readonly AppFactory factory;

    public RegisterEndpointTests(AppFactory factory) => this.factory = factory;

    /// <summary>The real app on its own temporary SQLite file. The "Testing" environment skips seeding.</summary>
    public sealed class AppFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"pf-ref-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ConsentIdentity", $"Data Source={databasePath}");
            builder.UseSetting("Entra:UseFakeDirectory", "true");
            // High enough that the other tests never trip it; the rate limit tests lower it.
            builder.UseSetting("RateLimiting:Registration:PermitLimit", "1000");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(databasePath) + "*"))
                    File.Delete(file);
        }
    }

    private HttpClient NewClient() => NewClient(factory);

    private static HttpClient NewClient(WebApplicationFactory<Program> source) =>
        source.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> GetAntiforgeryToken(HttpClient client)
    {
        var html = await client.GetStringAsync("/register");
        return Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(string? token, string name, string email)
    {
        var fields = new Dictionary<string, string>
        {
            ["_handler"] = "register",
            ["Model.Name"] = name,
            ["Model.Email"] = email,
        };
        if (token is not null)
            fields["__RequestVerificationToken"] = token;
        return new FormUrlEncodedContent(fields);
    }

    [Fact]
    public async Task The_form_page_renders_with_an_antiforgery_token()
    {
        var client = NewClient();

        Assert.False(string.IsNullOrEmpty(await GetAntiforgeryToken(client)));
    }

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_rejected()
    {
        var client = NewClient();
        await GetAntiforgeryToken(client); // gets the antiforgery cookie, but the token is deliberately not sent

        var response = await client.PostAsync("/register", Form(null, "Ada", "ada.no-token@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_post_redirects_to_the_list_with_the_new_id()
    {
        var client = NewClient();
        var token = await GetAntiforgeryToken(client);

        var response = await client.PostAsync("/register", Form(token, "Ada", "ada.valid@example.com"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Matches(@"^/registrations\?registered=[0-9a-fA-F-]{36}$", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task A_registration_is_only_recorded_as_submitted_and_nothing_is_sent_to_entra()
    {
        var client = NewClient();
        var response = await client.PostAsync("/register", Form(await GetAntiforgeryToken(client), "Ada", "ada.submitted@example.com"));
        var id = Guid.Parse(response.Headers.Location!.Query.Split('=')[1]);

        // Give the hosted workers a moment; they must not pick a Submitted row up.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var list = factory.Services.GetRequiredService<ListRegistrationsUseCase>();
        var item = (await list.Handle(new ListRegistrationsUseCase.Request(), CancellationToken.None)).Value.Items.Single(i => i.Id == id);
        Assert.Equal(RegistrationStatus.Submitted, item.Status);
    }

    [Fact]
    public async Task A_duplicate_email_stays_on_the_form_with_a_message_on_the_email_field()
    {
        var client = NewClient();
        await client.PostAsync("/register", Form(await GetAntiforgeryToken(client), "Ada", "ada.dup@example.com"));

        var response = await client.PostAsync("/register", Form(await GetAntiforgeryToken(client), "Other", "ADA.DUP@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A registration with this email already exists.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_input_stays_on_the_form_with_field_messages()
    {
        var client = NewClient();

        var response = await client.PostAsync("/register", Form(await GetAntiforgeryToken(client), "", "nope"));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Name is required.", html);
        Assert.Contains("Email is not a valid address.", html);
    }

    private HttpClient LimitedClient(int permitLimit)
    {
        var limited = factory.WithWebHostBuilder(b =>
            b.UseSetting("RateLimiting:Registration:PermitLimit", permitLimit.ToString()));
        return NewClient(limited);
    }

    [Fact]
    public async Task Submissions_over_the_limit_get_429_with_a_retry_after_header()
    {
        var client = LimitedClient(permitLimit: 2);

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            last = await client.PostAsync("/register", Form(await GetAntiforgeryToken(client), "Ada", $"ada.limit{i}@example.com"));
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.TooManyRequests], statuses);
        Assert.True(last!.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Viewing_the_form_is_not_counted_against_the_limit()
    {
        var client = LimitedClient(permitLimit: 1);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/register")).StatusCode);
    }

    [Fact]
    public async Task Pages_without_the_policy_are_never_limited()
    {
        var client = LimitedClient(permitLimit: 1);

        foreach (var path in new[] { "/", "/registrations", "/", "/registrations", "/" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }
}
