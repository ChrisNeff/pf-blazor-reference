using PF.BlazorReference.Core;
using PF.BlazorReference.Infrastructure;
using PF.BlazorReference.Infrastructure.Database;
using PF.BlazorReference.SharedLibrary;
using PF.BlazorReference.Web;
using PF.BlazorReference.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Each project registers what it owns with its own Add<ProjectName>(); Program.cs only composes them.
builder.Services
    .AddCore()
    .AddInfrastructure(builder.Configuration)
    .AddSharedLibrary()
    .AddWeb();

var app = builder.Build();

// Apply schema.sql (and, in Development, seed.sql) before the first request. Both are idempotent.
await DatabaseInitializer.InitializeAsync(
    builder.Configuration.GetConnectionString(PF.BlazorReference.Infrastructure.ServiceCollectionExtensions.ConnectionStringName)!,
    seed: app.Environment.IsDevelopment());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Enforces the policies pages opt into. After routing (WebApplication adds it implicitly) so the
// endpoint's policy can be found.
app.UseRateLimiter();

// Static SSR forms post back to the server; the antiforgery token EditForm emits is checked here.
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the test project.
public partial class Program;
