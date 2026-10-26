# Blazor reference app

A small Blazor Server app on .NET 10 that shows one design end to end: hexagonal architecture with vertical slices, hand-rolled use cases, a database-first SQLite schema, and a static-by-default UI with one interactive island. It registers a developer (name and email, with validation and a unique-email check) and, from the registrations grid, invites a registrant as an Entra guest in the background, activating them once they redeem the invitation.

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
| `PF.BlazorReference.Web` | Blazor Server host: `Program.cs`, pages, feature components, background services (workers) |
| `PF.BlazorReference.Core` | Entities, use cases, ports, query extensions. References no other project |
| `PF.BlazorReference.Infrastructure` | EF Core mappings, the DbContext, the audit and version interceptors, `schema.sql`, the Microsoft Graph adapter, the invitation queue |
| `PF.BlazorReference.SharedLibrary` | Razor class library: the shell, the nav island, CSS tokens. UI only |
| `PF.BlazorReference.Tests` | xUnit unit, architecture and HTTP-level tests |

- Dependencies point inward. Web and Infrastructure reference Core. Core references no other project and no database provider (it may reference `Microsoft.EntityFrameworkCore` for `DbSet<T>`). SharedLibrary references neither Core nor Infrastructure. Architecture tests enforce this.
- **DI registration:** each project exposes one top-level `Add<ProjectName>()` extension (`AddCore()`, `AddInfrastructure(IConfiguration)`, `AddSharedLibrary()`, `AddWeb()`) in `ServiceCollectionExtensions.cs` at its root, registering everything that project owns. `Program.cs` composes them, and a project never registers another project's services (the MudBlazor services belong to `AddSharedLibrary()`, not `AddWeb()`). An architecture test requires each project to expose exactly one.

## Use cases and ports

Slices mirror by folder name: `Web/Features/<Feature>/` holds the container page, its presentation components (`Components/`) and their view models (`ViewModels/`); `Core/Features/<Feature>/` holds its use cases. Background work follows the same shape: `Web/BackgroundServices/<Worker>/` holds a worker, which calls use cases from `Core/Features/<Feature>/` (see Background services).

Core is laid out by role:

- `Entities/`: one file per entity, enum and entity interface (`DeveloperRegistration`, `RegistrationStatus`, `IEntity`, `IAuditable`, `IVersioned`, `IClaimable`). Common properties are narrow interfaces an entity opts into, never a base class: `IEntity` (`Id`), `IAuditable`, `IVersioned`, and `IClaimable` (the worker lease, which implies `IEntity` and `IVersioned`). Features never define entities. A status change with rules is a method on the aggregate (`RequestProvisioning()`, `MarkInvited()`, `MarkActivated()`), never a setter.
- `Features/<Feature>/`: made of use-case classes and little else.
- `Extensions/`: reused queries (see Reused queries below).
- `Ports/`: one folder per port, holding the port interface and the exceptions it raises, one type per file (`Ports/Data/` has `IConsentIdentityDb`, `IConsentIdentityDbFactory`, `UniqueConstraintViolationException`, `ConcurrencyConflictException`; `Ports/Directory/` has `IUserManagementService` and `UserManagementException`). A port with a single file and no exception of its own sits directly in `Ports/` (`IInvitationQueue`). Namespaces mirror the folders.
- The Core root holds only `IUseCase` and `ServiceCollectionExtensions`; an architecture test enforces it.

A **use case** is one class named `<Verb><Noun>UseCase`, in `Core/Features/<Feature>/<Name>UseCase.cs`, implementing `IUseCase<TRequest, TResult>`. It nests its own `Request` and `Response` records. There is no mediator package. Register each use case with one line in `AddCore()`. Pages inject the concrete use-case class. An architecture test requires every type under `Features/` to be a `*UseCase`.

**A use case's request and response are its contract, and never expose an entity.** Results use the use case's own nested types: records (`ListRegistrationsUseCase.Item`), enums mapped from the domain inside the use case (`ListRegistrationsUseCase.Status`, `SendInvitationUseCase.Outcome`), and the domain's answers as flags (`Item.CanRequestInvitation`). Constants a caller needs are republished on the use case (`RegisterDeveloperUseCase.NameMaxLength`). Web never references `Core.Entities`; an architecture test enforces it, so a change to the domain model stops at the use case.

**Nested request and response records are always referenced through their container class**, including inside the container itself: `RegisterDeveloperUseCase.Request`, `ListRegistrationsUseCase.Item`. Never rely on the bare nested name, even where the compiler allows it, so a reader can see where a type comes from without scrolling.

- A **port** is a Core interface for anything external. Its adapter lives in Infrastructure. The data port is `IConsentIdentityDb`, created by `IConsentIdentityDbFactory`; each `Handle()` call creates its own short-lived context with `await using var db = await dbFactory.CreateAsync(ct);`. Contexts never live for a circuit.
- Expected failures return `ErrorOr<TResponse>`. Error **codes are field names** (`"Name"`, `"Email"`) so the UI can place them. Exceptions are for the unexpected.
- Enums reserve `0` as `Invalid`, so an unset value is never a valid state (`RegistrationStatus.Invalid = 0`). The domain never assigns it and the schema's `CHECK` rejects it.
- Domain entities are always-valid aggregates: the only way to get one is its factory, and state with rules has no public setter. The factory re-validates even though the form has annotations, because Core cannot trust its callers.
- A provider failure that means something to the domain is translated in Infrastructure into a Core exception (`UniqueConstraintViolationException`, `ConcurrencyConflictException`, `UserManagementException`), so Core never inspects a provider or SDK exception.
- **Reused queries** are extension methods on the data port (`IConsentIdentityDb`), in `Core/Extensions/<Aggregate>DbExtensions.cs`, one class per aggregate. A query used by a single use case stays inline in that use case; promote it when a second one needs it. Return `IQueryable<T>` when callers need to compose (paging, projection, tracking) and a `Task<T>` when the query is a complete question. They are tested against the real schema.
- Cross-cutting concerns stay out of use cases: audit timestamps come from a `SaveChangesInterceptor`, logging comes from the host.
- Ask before adding a NuGet package.

## Background services

Workers are laid out like features: one folder per worker, and the business logic in Core use cases.

```
Web/BackgroundServices/
  PeriodicWorker.cs                      base: timer, a DI scope per unit of work, catch and log
  ClaimOwner.cs                          this host and process, for row claims
  BackgroundServicesServiceCollectionExtensions.cs   AddBackgroundServices(), called by AddWeb()
  <Worker>/
    <Name>Worker.cs                      the BackgroundService
    <Name>Options.cs                     its schedule, bound from BackgroundServices:<Worker>
```

- **A worker is the background counterpart of a container page.** It owns when work runs, the DI scope, failure isolation and logging. It calls Core use cases and decides nothing about a row: what happens to the data is the use case's decision. A worker injects only Core interfaces, its options, `IServiceScopeFactory`, `TimeProvider` and a logger, and resolves use cases per unit of work.
- **Every worker derives from `PeriodicWorker`.** It runs `RunOnceAsync` at startup and then every interval, and `InScopeAsync<TUseCase>` runs one unit of work in its own scope and logs and swallows any exception, because an unhandled exception in a `BackgroundService` stops the host. A worker that also reads a queue runs that loop next to `base.ExecuteAsync`.
- **A cancellation is logged at Information, never swallowed silently.** Catch `OperationCanceledException` only `when` the token was cancelled, and say what stopped and that no work is lost (it stays on its row). `InScopeAsync` names the interrupted unit of work and rethrows; the loop that ends logs that the worker stopped. A shutdown is not a failure, so it never logs at Warning or above. The same applies to a page's own loops (`RegistrationsPage` logs when its auto-refresh stops because the page closed).
- **Keep per-row loops in the worker** when one row's unexpected failure must not stop the others (the acceptance check lists ids with one use case and runs another per id). A sweep that is one database question plus queueing is a use case (`RequeueInvitationRequestsUseCase`).
- **Registration:** `AddBackgroundServices()` binds each worker's options and adds it with `AddHostedService`. A queue a worker reads is a Core interface implemented in Infrastructure, and `AddInfrastructure` registers it.
- **Architecture tests:** every hosted service in Web is named `*Worker`, lives in `Web/BackgroundServices/<Worker>/` and derives from `PeriodicWorker`. Nothing under `BackgroundServices` depends on Infrastructure, EF Core or Web's features.
- To add a worker: create the folder with the worker and its options, add its use cases under `Core/Features/<Feature>/` (one line each in `AddCore()`), add two lines to `AddBackgroundServices()` and the settings to `appsettings.json`, and test it with `FakeTimeProvider` like `Tests/Web/BackgroundServices/`.

## Entra guest invitations and background work

The form only records a registration, as **Submitted**. From the grid, a button requests a **B2B guest invitation** in the workforce tenant (`POST /invitations`). The guest is **unusable until the email is proven, then turned on**: redeeming the invitation proves the address (Entra may ask for a one-time code), and only then is the guest added to the activated group, which is the access.

```
Submitted (form) → ProvisioningRequested (grid button) → Invited (invitation worker: sent, no access)
                 → Activated (acceptance check: redeemed, added to Entra:ActivatedGroupId)
ProvisioningRequested → ProvisioningFailed (the grid's button retries)
```

- **The guest account is enabled, not disabled.** Redeeming an invitation is a sign-in, and a disabled account cannot sign in, so a disabled guest could never prove the email. Access is withheld by group membership instead; the apps (or a Conditional Access policy) require the group.
- **Two workers** (see Background services), calling the use cases in `Core/Features/Invitations/`:
  - `BackgroundServices/Invitations/InvitationWorker` reads the queue the grid's button fills and runs `SendInvitationUseCase`. Its periodic run, at startup and every `BackgroundServices:Invitations:SweepIntervalSeconds`, is `RequeueInvitationRequestsUseCase`, which recovers lost signals (a restart, a full queue, a request made on another host).
  - `BackgroundServices/AcceptanceCheck/AcceptanceCheckWorker` runs at startup and every `BackgroundServices:AcceptanceCheck:IntervalSeconds` (a fixed interval, not a cron expression). It lists `Invited` rows and runs `ActivateAcceptedGuestUseCase`, which reads the guest's `externalUserState` and activates on `Accepted`. Polling, because Entra does not call back. An invitation never redeemed is checked forever; nothing expires it yet.
- **The row is the record of the work; the queue is only a wake-up.** `RequestInvitationUseCase` sets `ProvisioningRequested`, saves, and only then calls `IInvitationQueue.TryEnqueue`. The queue (`Infrastructure/Queues/ChannelInvitationQueue`, registered by `AddInfrastructure`) is a bounded in-memory `Channel<Guid>` per host; the worker reads it through the same Core interface (`ReadAllAsync`).
- **Several hosts.** Every host runs both workers, so a row is **claimed before Entra is written to**:
  - `DeveloperRegistration` is `IVersioned`: `Version` is an EF concurrency token, incremented on every update by `VersionInterceptor` (SQL Server would use `rowversion`). A save based on a stale read throws `ConcurrencyConflictException`, translated from `DbUpdateConcurrencyException` in the DbContext.
  - `ClaimAsync` (`Core/Extensions/DeveloperRegistrationDbExtensions`; the claim and save itself is the generic `TryClaimAndSaveAsync` in `ClaimableDbExtensions`, for any `IClaimable`) loads the row, checks its status, sets `ClaimedBy` (host and process, `ClaimOwner.Current`) and `ClaimedUntilUtc`, and saves. It returns null when another owner's lease is live or another host saved first; the use case then skips the row.
  - The lease (5 minutes) lapses, so a row held by a crashed host is picked up again. Every worker outcome releases the claim.
  - `SendInvitationUseCase` claims before inviting. `ActivateAcceptedGuestUseCase` checks acceptance with a read and claims only when there is something to write, so idle checks write nothing.
  - The grid's request also saves under the token, so two people clicking at once get one request and one conflict message.
  - SQLite runs in WAL mode (set by `DatabaseInitializer`), so two processes sharing the file do not refuse each other's writes.
- **Every directory call can be repeated.** Before inviting, the adapter looks for a guest with the same email (`mail eq ... and userType eq 'Guest'`) and reuses it, so a lapsed claim that leads to a second attempt does not send a second email. Adding a guest who is already a member counts as success.
- **Failures:** `UserManagementException` is expected. A failed invitation marks the row `ProvisioningFailed` with the reason, and the grid offers a retry. A failed activation records the reason but keeps the row `Invited` for the next check. A failed acceptance check records nothing. Any other exception changes nothing; the claim lapses and a later sweep retries.
- **Microsoft Graph is used only by `GraphUserManagementService` in Infrastructure.** It builds its own `GraphServiceClient` (`DefaultAzureCredential`), never registers or exposes it, and translates every SDK failure into `UserManagementException`. `Microsoft.Graph` and `Azure.Identity` are referenced with `PrivateAssets="compile"`, so projects that reference Infrastructure cannot compile against them. Architecture tests enforce both rules. The app's identity needs the Graph application permissions `User.Invite.All`, `User.Read.All` and `GroupMember.ReadWrite.All`.
- **Configuration** (`Entra`, no secrets): `UseFakeDirectory` is true in Development and tests (`FakeUserManagementService` sends nothing and treats every invitation as redeemed). Otherwise `InviteRedirectUrl` and `ActivatedGroupId` are required at startup, with optional `TenantId` and `ManagedIdentityClientId`. `DryRun` (on unless set to `false`) authenticates and reads but never invites or changes a group; a dry-run invitation marks the row `ProvisioningFailed` with a "Dry run" reason, so it can be retried once the switch is off.
- **`/registrations` follows the invitation worker.** After a request, while any row is `ProvisioningRequested`, the page re-reads the list every 3 seconds, for at most 2 minutes, until each is `Invited` or `ProvisioningFailed`. `Invited` to `Activated` is not polled; the refresh button shows it. The status chip's tooltip shows the failure reason.
- **The invite button is shown only in Development** because `/registrations` is public. Replace that check with an authorization policy when authentication arrives. The public form sends nothing to Entra.

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
- Components split into **container** and **presentation** (Dan Abramov's names for "smart" and "dumb" components: containers deal with how things work, presentation components with how things look).
  - A **container** is the routable page. It owns the route, the render mode, state, and the use-case calls. It maps each use-case result to view models and passes those down. Only containers inject services.
  - A **presentation** component takes `[Parameter]` in, raises `EventCallback` out, and has no `@inject` / `[Inject]`. They live in `Features/<Feature>/Components/` or in SharedLibrary. An architecture test enforces this.
  - A presentation component **binds view models, never Core types**: no use-case contract, no entity, no domain rule. An architecture test enforces this for `Features/<Feature>/Components/` (and the SharedLibrary rule keeps the library off Core entirely). The only state it keeps is UI state, such as a search box or an open drawer.
- **View models** live in `Features/<Feature>/ViewModels/`, one record per thing a presentation component shows, with everything already decided: labels, colors, hints, enabled flags (`RegistrationRow`). A static `From(...)` builds one from a use-case result. Wording and styling are presentation decisions made there; business decisions arrive from the use case as flags, never recomputed. View models are unit-tested as plain data (`Tests/Web/Features/`).
- `.razor` holds markup only; `.razor.cs` is the partial class with `[Parameter]`, `[Inject]`, state and handlers. No `@code` or `@functions` block, in Web or SharedLibrary; a test reads the files and enforces it.
- Page state is a page-local record of view models, for example `RegistrationsState(Rows, Loading, Error)`.
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
- Architecture tests use NetArchTest plus reflection for the presentation-component and DI-registration rules, and read the `.razor` files for the markup-only rule.
- The workers are tested with the real queue and use cases over `FakeConsentIdentityDb` and a substitute `IUserManagementService` (`Tests/Web/BackgroundServices/`), with `FakeTimeProvider` driving the schedules. The claim and the concurrency token are tested against real SQLite, including two contexts that read the same version (`DeveloperRegistrationDbExtensionsTests`). No test calls Graph: its pure parts (the guest lookup filter) and its registration are tested, and the request itself is checked by hand against a test tenant.
- There are no component tests (bUnit): the toast and interactive behaviour are checked by hand.

## Not in this reference, on purpose

No authentication or authorization, no general outbox (the two provisioning workers are the only background processors, see above), no `/health` endpoint, no CI, no Dockerfile. Add them in a separate change with their own tests.
