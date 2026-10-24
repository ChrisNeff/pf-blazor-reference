using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Hosting;
using NetArchTest.Rules;
using PF.BlazorReference.Core;
using PF.BlazorReference.Infrastructure;
using PF.BlazorReference.SharedLibrary;
using PF.BlazorReference.Web.BackgroundServices;
using PF.BlazorReference.Web.Features.Registration.Components;

namespace PF.BlazorReference.Tests.Architecture;

/// <summary>The mechanical rules of the design. Dependencies point inward; presentation components inject nothing.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(IUseCase<,>).Assembly;
    private static readonly Assembly Infrastructure = typeof(ConsentIdentityDbContext).Assembly;
    private static readonly Assembly SharedLibrary = typeof(NavItem).Assembly;
    private static readonly Assembly Web = typeof(RegistrationForm).Assembly;

    private const string CoreNs = "PF.BlazorReference.Core";
    private const string InfrastructureNs = "PF.BlazorReference.Infrastructure";
    private const string SharedLibraryNs = "PF.BlazorReference.SharedLibrary";
    private const string WebNs = "PF.BlazorReference.Web";

    [Fact]
    public void Core_references_no_other_project()
    {
        var result = Types.InAssembly(Core).ShouldNot()
            .HaveDependencyOnAny(InfrastructureNs, SharedLibraryNs, WebNs)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Core_references_no_database_provider()
    {
        // The base Microsoft.EntityFrameworkCore package is allowed (for DbSet<T>); providers are not.
        var result = Types.InAssembly(Core).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.Data.Sqlite", "Microsoft.Data.SqlClient")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_reference_the_ui_projects()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot()
            .HaveDependencyOnAny(SharedLibraryNs, WebNs)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static readonly string[] GraphNamespaces = ["Microsoft.Graph", "Microsoft.Kiota", "Azure.Identity", "Azure.Core"];

    [Fact]
    public void Only_Infrastructure_references_Microsoft_Graph()
    {
        var offenders = new[] { Core, SharedLibrary, Web }
            .SelectMany(assembly => Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(GraphNamespaces).GetResult().FailingTypeNames ?? [])
            .ToList();

        Assert.True(offenders.Count == 0, "Microsoft Graph is used only in Infrastructure: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Within_Infrastructure_only_GraphUserManagementService_touches_the_Graph_client()
    {
        // The service builds its own GraphServiceClient and never hands it out. Its compiler-generated
        // nested types (async state machines, lambdas) count as the service.
        const string owner = "PF.BlazorReference.Infrastructure.UserManagement.GraphUserManagementService";
        var users = Types.InAssembly(Infrastructure).That().HaveDependencyOnAny(GraphNamespaces).GetTypes().ToList();

        Assert.Contains(users, t => t.FullName == owner); // guard against passing vacuously
        var offenders = users.Where(t => t.FullName != owner && t.FullName?.StartsWith(owner + "+", StringComparison.Ordinal) != true);
        Assert.True(!offenders.Any(), "Only GraphUserManagementService may use Microsoft Graph: " + string.Join(", ", offenders.Select(t => t.FullName)));
    }

    [Fact]
    public void Web_reaches_the_database_only_through_use_cases()
    {
        // Pages and the provisioning worker call use cases; none opens the data port or the DbContext.
        var result = Types.InAssembly(Web).ShouldNot()
            .HaveDependencyOnAny(typeof(IConsentIdentityDb).FullName!, typeof(IConsentIdentityDbFactory).FullName!, typeof(ConsentIdentityDbContext).FullName!)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private const string BackgroundServicesNs = WebNs + ".BackgroundServices";

    [Fact]
    public void Background_services_are_workers_each_in_its_own_folder_under_BackgroundServices()
    {
        // The counterpart of Features/<Feature>/ for pages: Web/BackgroundServices/<Worker>/<Name>Worker.
        var services = Web.GetTypes()
            .Where(t => typeof(IHostedService).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        // Guard against passing vacuously.
        Assert.Contains(services, t => t.Name == "InvitationWorker");
        Assert.Contains(services, t => t.Name == "AcceptanceCheckWorker");

        var misplaced = services.Where(t =>
                !t.Name.EndsWith("Worker", StringComparison.Ordinal)
                || t.Namespace is not { } ns
                || !ns.StartsWith(BackgroundServicesNs + ".", StringComparison.Ordinal)
                || ns.Count(c => c == '.') != BackgroundServicesNs.Count(c => c == '.') + 1)
            .Select(t => t.FullName);
        Assert.True(!misplaced.Any(),
            "Background services must be named *Worker and live in Web/BackgroundServices/<Worker>/: " + string.Join(", ", misplaced));

        var notPeriodic = services.Where(t => !typeof(PeriodicWorker).IsAssignableFrom(t)).Select(t => t.Name);
        Assert.True(!notPeriodic.Any(), "Workers derive from PeriodicWorker, which owns scopes and failure handling: " + string.Join(", ", notPeriodic));
    }

    [Fact]
    public void Workers_reach_only_Core_use_cases_and_ports()
    {
        // A worker decides when to call a use case and what to log; what happens to a row is Core's call.
        var result = Types.InAssembly(Web).That().ResideInNamespaceStartingWith(BackgroundServicesNs)
            .ShouldNot().HaveDependencyOnAny(InfrastructureNs, "Microsoft.EntityFrameworkCore", WebNs + ".Features")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void SharedLibrary_references_neither_Core_nor_Infrastructure()
    {
        var result = Types.InAssembly(SharedLibrary).ShouldNot()
            .HaveDependencyOnAny(CoreNs, InfrastructureNs)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [InlineData(typeof(IUseCase<,>), "AddCore")]
    [InlineData(typeof(ConsentIdentityDbContext), "AddInfrastructure")]
    [InlineData(typeof(NavItem), "AddSharedLibrary")]
    [InlineData(typeof(RegistrationForm), "AddWeb")]
    public void Each_project_registers_what_it_owns_with_its_own_Add_ProjectName_extension(Type projectMarker, string methodName)
    {
        var assembly = projectMarker.Assembly;

        var extensions = assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: true, IsSealed: true })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name == methodName
                        && m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false)
                        && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection)
                        && m.ReturnType == typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection))
            .ToList();

        Assert.True(extensions.Count == 1,
            $"{assembly.GetName().Name} must expose exactly one {methodName}(this IServiceCollection ...) returning IServiceCollection, found {extensions.Count}.");
    }

    [Fact]
    public void Core_features_are_made_of_use_case_classes()
    {
        var featureTypes = Core.GetTypes()
            .Where(t => t.Namespace?.StartsWith(CoreNs + ".Features", StringComparison.Ordinal) == true
                        && !t.IsNested
                        && !t.Name.StartsWith('<'))
            .ToList();

        Assert.NotEmpty(featureTypes);
        var misnamed = featureTypes.Where(t => !t.Name.EndsWith("UseCase", StringComparison.Ordinal)).Select(t => t.Name);
        Assert.True(!misnamed.Any(), "Types under Core/Features must be use cases named *UseCase: " + string.Join(", ", misnamed));

        var notUseCases = featureTypes.Where(t => !t.GetInterfaces().Any(
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUseCase<,>))).Select(t => t.Name);
        Assert.True(!notUseCases.Any(), "Types named *UseCase must implement IUseCase<,>: " + string.Join(", ", notUseCases));
    }

    [Fact]
    public void Presentation_components_inject_nothing()
    {
        var presentation = PresentationComponents().ToList();

        // Guard against the rule passing vacuously if a folder or namespace convention changes.
        Assert.Contains(presentation, t => t == typeof(RegistrationForm));
        Assert.Contains(presentation, t => t == typeof(RegistrationTable));
        Assert.Contains(presentation, t => t == typeof(AppShell));

        var offenders = presentation
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null)
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Presentation components must not inject services: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Interactive_components_take_only_parameters_that_can_cross_the_render_mode_boundary()
    {
        // A component with a render mode is an interactive root: when a static parent renders it, its
        // parameters are serialized by their runtime type and deserialized on the circuit. An interface
        // type lets a hidden compiler type through (a collection expression assigned to IReadOnlyList<T>
        // is the classic case), and the whole circuit fails to start. Delegates and EventCallback cannot
        // be serialized at all.
        var interactiveRoots = SharedLibrary.GetTypes().Concat(Web.GetTypes())
            .Where(IsComponent)
            .Where(t => t.GetCustomAttributes(inherit: true).Any(a => a is RenderModeAttribute))
            .ToList();

        // Guard against the rule passing vacuously.
        Assert.Contains(interactiveRoots, t => t.Name == "NavIsland");
        Assert.Contains(interactiveRoots, t => t.Name == "RegistrationsPage");

        var offenders = interactiveRoots
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetCustomAttribute<ParameterAttribute>() is { CaptureUnmatchedValues: false })
                .Select(p => (Type: t, Property: p, ParameterType: Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
                .Where(x => !CanCrossRenderModeBoundary(x.ParameterType))
                .Select(x => $"{x.Type.Name}.{x.Property.Name} ({x.ParameterType.Name})"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Interactive component parameters must be concrete, serializable types (use an array or List<T>, not an interface; no delegates or EventCallback): "
            + string.Join(", ", offenders));
    }

    private static bool CanCrossRenderModeBoundary(Type type) =>
        !type.IsInterface
        && !(type.IsAbstract && !type.IsSealed) // abstract classes; arrays and static types are fine
        && !typeof(Delegate).IsAssignableFrom(type)
        && type != typeof(EventCallback)
        && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EventCallback<>));

    /// <summary>Everything in the shared library, plus components under Features/&lt;Feature&gt;/Components in an app.</summary>
    private static IEnumerable<Type> PresentationComponents() =>
        SharedLibrary.GetTypes().Where(IsComponent)
            .Concat(Web.GetTypes().Where(t => IsComponent(t)
                && t.Namespace is { } ns && ns.Contains(".Features.") && ns.EndsWith(".Components")));

    private static bool IsComponent(Type type) => typeof(IComponent).IsAssignableFrom(type) && !type.IsAbstract;

    private static string Describe(TestResult result) =>
        "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
