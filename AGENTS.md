# Blazor reference app

A small Blazor Server app on .NET 10 that shows one design end to end: hexagonal architecture with vertical slices, hand-rolled use cases, a database-first SQLite schema, and a static-by-default UI with one interactive island. It registers a developer (name and email, with validation and a unique-email check) and can invite a registrant as an Entra guest in the background, activating them once they redeem the invitation.

Everything below is implemented in this repo. If you change a convention, update this file in the same change.

## Build and test

```bash
dotnet build PF.BlazorReference.slnx
dotnet test PF.BlazorReference.slnx
dotnet run --project PF.BlazorReference.Web      # http://localhost:5027
```

A change is done when the build is clean and all tests pass, architecture tests included. Every new use case has tests.

## Projects

| Project | Role |
|---------|------|
| `PF.BlazorReference.Web` | Blazor Server host: `Program.cs`, pages, feature components, background workers |
| `PF.BlazorReference.Core` | Entities, use cases, ports, query extensions. References no other project |
| `PF.BlazorReference.Infrastructure` | EF Core mappings, the DbContext, the audit interceptor, `schema.sql`, the Microsoft Graph adapter |
| `PF.BlazorReference.SharedLibrary` | Razor class library: the shell, the nav island, CSS tokens. UI only |
| `PF.BlazorReference.Tests` | xUnit unit, architecture and HTTP-level tests |

- Dependencies point inward. Web and Infrastructure reference Core. Core references no other project and no database provider (it may reference `Microsoft.EntityFrameworkCore` for `DbSet<T>`). SharedLibrary references neither Core nor Infrastructure. Architecture tests enforce this.
- **DI registration:** each project exposes one top-level `Add<ProjectName>()` extension (`AddCore()`, `AddInfrastructure(IConfiguration)`, `AddSharedLibrary()`, `AddWeb()`) in `ServiceCollectionExtensions.cs` at its root, registering everything that project owns. `Program.cs` composes them, and a project never registers another project's services (the MudBlazor services belong to `AddSharedLibrary()`, not `AddWeb()`). An architecture test requires each project to expose exactly one.

## Use cases and ports

Slices mirror by folder name: `Web/Features/<Feature>/` holds the container page and its presentation components, `Core/Features/<Feature>/` holds its use cases.

Core is laid out by role:

- `Entities/`: one file per entity, enum and entity interface (`DeveloperRegistration`, `RegistrationStatus`, `IAuditable`). Features never define entities. A status change with rules is a method on the aggregate (`RequestProvisioning()`, `MarkInvited()`, `MarkActivated()`), never a setter.
- `Features/<Feature>/`: made of use-case classes and little else.
- `Extensions/`: reused queries (see Reused queries below).
- The Core root holds the ports and shared abstractions, one type per file.

A **use case** is one class named `<Verb><Noun>UseCase`, in `Core/Features/<Feature>/<Name>UseCase.cs`, implementing `IUseCase<TRequest, TResult>`. It nests its own `Request` and `Response` records. There is no mediator package. Register each use case with one line in `AddCore()`. Pages inject the concrete use-case class. An architecture test requires every type under `Features/` to be a `*UseCase`.

**Nested request and response records are always referenced through their container class**, including inside the container itself: `RegisterDeveloperUseCase.Request`, `ListRegistrationsUseCase.Item`. Never rely on the bare nested name, even where the compiler allows it, so a reader can see where a type comes from without scrolling.

- A **port** is a Core interface for anything external. Its adapter lives in Infrastructure. The data port is `IConsentIdentityDb`, created by `IConsentIdentityDbFactory`; each `Handle()` call creates its own short-lived context with `await using var db = await dbFactory.CreateAsync(ct);`. Contexts never live for a circuit.
- Expected failures return `ErrorOr<TResponse>`. Error **codes are field names** (`"Name"`, `"Email"`) so the UI can place them. Exceptions are for the unexpected.
- Enums reserve `0` as `Invalid`, so an unset value is never a valid state (`RegistrationStatus.Invalid = 0`). The domain never assigns it and the schema's `CHECK` rejects it.
- Domain entities are always-valid aggregates: the only way to get one is its factory, and state with rules has no public setter. The factory re-validates even though the form has annotations, because Core cannot trust its callers.
- A provider failure that means something to the domain is translated in Infrastructure into a Core exception (`UniqueConstraintViolationException`, `UserManagementException`), so Core never inspects a provider or SDK exception.
- **Reused queries** are extension methods on the data port (`IConsentIdentityDb`), in `Core/Extensions/<Aggregate>DbExtensions.cs`, one class per aggregate. A query used by a single use case stays inline in that use case; promote it when a second one needs it. Return `IQueryable<T>` when callers need to compose (paging, projection, tracking) and a `Task<T>` when the query is a complete question. They are tested against the real schema.
- Cross-cutting concerns stay out of use cases: audit timestamps come from a `SaveChangesInterceptor`, logging comes from the host.
- Ask before adding a NuGet package.

## Entra guest invitations and background work

The grid's "Invite" button invites a registrant as a **B2B guest** in the workforce tenant (`POST /invitations`). The guest is **unusable until the email is proven, then turned on**: redeeming the invitation proves the address (Entra may ask for a one-time code), and only then is the guest added to the activated group, which is the access.

```
Pending → ProvisioningRequested (button) → Invited (invitation sent, guest exists, no access)
        → Activated (redeemed, added to Entra:ActivatedGroupId)
ProvisioningRequested → ProvisioningFailed (invitation failed; the button retries)
```

- **The guest account is enabled, not disabled.** Redeeming an invitation is a sign-in, and a disabled account cannot sign in, so a disabled guest could never prove the email. Access is withheld by group membership instead; the apps (or a Conditional Access policy) require the group.
- **The row is the record of the work; the queue is only a wake-up.** `RequestEntraProvisioningUseCase` sets `ProvisioningRequested`, saves, and only then calls `IProvisioningQueue.TryEnqueue`. The queue (`Web/Provisioning/ProvisioningQueue`) is a bounded in-memory `Channel<Guid>`; a full queue or a restart loses only the signal.
- **Redemption is noticed by polling.** Entra does not call back. `ProvisioningWorker` sweeps the table at startup and every `SweepInterval` (5 minutes) for `ProvisioningRequested` and `Invited` rows (`ListProvisioningBacklogUseCase`) and enqueues them. For an `Invited` row, the use case reads the guest's `externalUserState` and activates on `Accepted`. An invitation never redeemed is polled forever; nothing expires it yet.
- **One use case moves a row one step.** `ProvisionDeveloperUseCase` invites a `ProvisioningRequested` row, activates a redeemed `Invited` row, and leaves anything else alone, so running it twice is safe.
- **The worker is thin.** It is a `BackgroundService` in `Web/Provisioning/`, registered in `AddWeb()`. It runs the use case in a fresh DI scope, one id at a time, logs the outcome, and catches every exception per item, because an unhandled one stops the host. It never touches the data port; an architecture test keeps Web off it.
- **Every directory call can be repeated.** Before inviting, the adapter looks for a guest with the same email (`mail eq ... and userType eq 'Guest'`) and reuses it, so a crash between sending and saving does not send a second email. Adding a guest who is already a member counts as success.
- **One worker per app instance.** Several instances would need each row claimed before processing; that is not built.
- **Failures:** `UserManagementException` is expected. A failed invitation marks the row `ProvisioningFailed` with the reason, and the button offers a retry. A failed activation records the reason but keeps the row `Invited`, so the next sweep retries it. Any other exception changes nothing, and the next sweep retries.
- **Microsoft Graph is used only by `GraphUserManagementService` in Infrastructure.** It builds its own `GraphServiceClient` (`DefaultAzureCredential`), never registers or exposes it, and translates every SDK failure into `UserManagementException`. `Microsoft.Graph` and `Azure.Identity` are referenced with `PrivateAssets="compile"`, so projects that reference Infrastructure cannot compile against them. Architecture tests enforce both rules. The app's identity needs the Graph application permissions `User.Invite.All`, `User.Read.All` and `GroupMember.ReadWrite.All`.
- **Configuration** (`Entra`, no secrets): `UseFakeDirectory` is true in Development and tests (`FakeUserManagementService` sends nothing and treats every invitation as redeemed). Otherwise `InviteRedirectUrl` and `ActivatedGroupId` are required at startup, with optional `TenantId` and `ManagedIdentityClientId`. `DryRun` (on unless set to `false`) authenticates and reads but never invites or changes a group; a dry-run invitation marks the row `ProvisioningFailed` with a "Dry run" reason, so it can be retried once the switch is off.
- **The button is shown only in Development** because `/registrations` is public. Replace that check with an authorization policy when authentication arrives.

## Data

- **Database-first.** `Infrastructure/Database/schema.sql` is the schema source of truth. EF Core never creates or migrates it, and there are no EF migrations. It is idempotent and runs at startup and in every test. It only creates what is missing, so after changing a table delete the local `App_Data/reference.db` once.
- A status added to `RegistrationStatus` must be added to the `CHECK` in `schema.sql`; a test inserts every valid status.
- EF mappings are hand-written `IEntityTypeConfiguration<T>` classes, applied with `ApplyConfigurationsFromAssembly`. Never scaffold them into source.
- A constraint the schema owns (the unique email index) is declared in `schema.sql`, not in the EF mapping.
- `seed.sql` holds Development-only sample rows and is raw SQL. Data in code, tests, fixtures and docs is synthetic (`example.com` addresses).
- The SQLite file is `App_Data/reference.db`, gitignored.

## Blazor pages

- **Static SSR by default.** `Routes.razor` sets no render mode. A page that needs interactivity declares its own `@rendermode`.
- The layout is static. The only interactive piece is `NavIsland` (app bar and drawer), which also hosts the MudBlazor providers.
- Nav items are passed in by the app, as `NavItem[]`.
- Parameters of an interactive component (any component with a `@rendermode`, including a routable page) must be concrete serializable types: primitives, enums, records, arrays or `List<T>`. No interfaces, delegates, `RenderFragment` or `EventCallback`. An architecture test enforces this and explains why when it fails.
- Components split into **container** and **presentation**.
  - A **container** is the routable page. It owns the route, the render mode, state, and the use-case calls, and passes data down. Only containers inject services.
  - A **presentation** component takes `[Parameter]` in, raises `EventCallback` out, and has no `@inject` / `[Inject]`. They live in `Features/<Feature>/Components/` or in SharedLibrary. An architecture test enforces this.
- `.razor` holds markup; `.razor.cs` is the partial class with `[Inject]`, state and handlers.
- Page state is a page-local record, for example `RegistrationsState(Items, Loading, Error)`.
- Static pages use `EditForm` with a `FormName` and `[SupplyParameterFromForm]`. The posted field names are prefixed by the presentation component's `Model` parameter, so the container binds with `Name = RegistrationForm.FieldPrefix`.
- MudBlazor inputs need a circuit, so static pages use plain HTML inputs styled by CSS tokens. Interactive pages use MudBlazor freely.
- Branding is CSS tokens in `SharedLibrary/wwwroot/pf-tokens.css`, not a C# `MudTheme` class.

## Rate limiting

ASP.NET Core's rate limiter, scoped to **public, unauthenticated write paths only**. The registration form is the only one. Pages behind sign-in will get per-user limits when authentication arrives; there is deliberately no global per-address limit.

- Policies are registered in `AddWeb()` through `AddRateLimiting()` (`Web/RateLimiting/`). `app.UseRateLimiter()` is in `Program.cs`, after the implicit routing so the endpoint's policy is found.
- A page opts in with `[EnableRateLimiting(RateLimitPolicies.Registration)]`. `RegisterPage` is the only one.
- `RateLimitPolicies.Registration` is a **sliding window** (6 segments) per client, counting **POSTs only**: 5 per 60 s by default. Showing the form is never counted. A sliding window gives a client no double burst at a window boundary.
- The client key is the remote address (`ClientKey`): IPv4-mapped IPv6 is treated as the IPv4 address, and IPv6 clients are counted by their `/64`, because one client controls a whole `/64` and could otherwise rotate addresses.
- Limits come from configuration (`RateLimiting:Registration:PermitLimit` and `WindowSeconds`), so tests and environments can override them.
- A rejected request gets 429 with `Retry-After`. Sliding window leases carry no retry hint, so the value is the policy's full window.
- To limit another public write path: add a name to `RateLimitPolicies`, a property to `RateLimitingOptions`, a policy in `AddRateLimiting`, the `appsettings.json` values, and the attribute on the page.
- Behind a reverse proxy the remote address is the proxy's unless forwarded headers are configured, and every client would share one bucket. Configure them before relying on this in production.
- **This protects a public form from one abusive client. It does not stop a volumetric distributed attack**, which has to be absorbed at the edge (a CDN, WAF or application gateway in front of the app).

## Tests

- xUnit and NSubstitute. Warnings are errors; a change is done when the build is clean and every test passes.
- **Use-case unit tests use no database.** `FakeConsentIdentityDb` substitutes `IConsentIdentityDbFactory`, and the `IConsentIdentityDb` it returns is a substitute too. Its `DbSet` is backed by an in-memory list behind an async query provider, so `AnyAsync` and `ToListAsync` work. Seed `Registrations`; assert on `Registrations` (saved), `Set.Received().Add(...)` and `Db.Received().SaveChangesAsync(...)`. To force a failure, configure the substitute (`Db.SaveChangesAsync(...).ThrowsAsync(...)`).
- **What that cannot prove.** LINQ over a list is not SQL, so the substitute says nothing about query translation, mappings, constraints or the interceptor. Those are tested against a real in-memory SQLite database built from `schema.sql` (`SqliteTestDatabase`) in `Tests/Infrastructure/`: the schema mappings, the unique-index translation, the audit interceptor, and the query extension methods. Use SQLite there, never in a use-case test.
- Use `FakeTimeProvider` for time. Code that needs the time takes a `TimeProvider`.
- HTTP-level tests use `WebApplicationFactory<Program>` in the `Testing` environment (which skips seeding) against a temporary database file.
- Architecture tests use NetArchTest plus reflection for the presentation-component and DI-registration rules.
- The worker is tested with the real queue and use cases over `FakeConsentIdentityDb` and a substitute `IUserManagementService` (`Tests/Web/ProvisioningWorkerTests.cs`), with `FakeTimeProvider` driving the sweep. No test calls Graph: its pure parts (the guest lookup filter) and its registration are tested, and the request itself is checked by hand against a test tenant.
- There are no component tests (bUnit): the toast and interactive behaviour are checked by hand.

## Not in this reference, on purpose

No authentication or authorization, no general outbox (the provisioning worker is the only background processor, see above), no `/health` endpoint, no CI, no Dockerfile. Add them in a separate change with their own tests.
