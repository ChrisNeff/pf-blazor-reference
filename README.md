# Blazor reference app

A small, runnable Blazor Server app (.NET 10) that shows one design end to end, against a local SQLite database. It registers a developer with a name and email, with validation and a unique-email check, and, from the registrations grid, can invite a registrant as an Entra B2B guest in the background, giving the guest access only once they redeem the invitation (which proves the email).

It exists to prove that the design's conventions compile, hold together and can be tested, and to give a working example to copy from. The code stays small, and comments explain only the non-obvious parts.

## Run it

Requires the .NET 10 SDK.

```bash
dotnet run --project PF.BlazorReference.Web      # http://localhost:5027
dotnet test PF.BlazorReference.slnx
```

On first run the app creates `App_Data/reference.db` from `schema.sql`. In Development it also loads four sample rows from `seed.sql`. `schema.sql` only creates what is missing, so after a schema change delete `App_Data/reference.db` once to rebuild it.

In Development, invitations go to a fake directory (`Entra:UseFakeDirectory`), so no tenant is needed; it treats every invitation as redeemed. To use a real workforce tenant, set `Entra:UseFakeDirectory` and `Entra:DryRun` to `false`, plus `Entra:InviteRedirectUrl` and `Entra:ActivatedGroupId` (and optionally `Entra:TenantId`) in user secrets, and sign in with an identity that has the Graph permissions `User.Invite.All`, `User.Read.All` and `GroupMember.ReadWrite.All`.

## What to look at

| Page | Render mode | Shows |
|------|-------------|-------|
| `/` | Static SSR | The shell with no circuit open for the page |
| `/register` | Static SSR | `EditForm` posting to the server: annotation messages, domain errors on the right field, antiforgery, redirect after success. Registering only records the row, as Submitted |
| `/registrations` | `InteractiveServer` | A sortable, filterable MudBlazor table, a toast after a redirect from `/register`, and (in Development) status that follows the invitation worker (Sending invite, Invited, Failed with its reason), and (in Development) a button that invites a Submitted row or retries a failed one |

## Architecture in one screen

```
Web ──────────► Core ◄────────── Infrastructure
 │               ▲  use cases, aggregate, ports (IConsentIdentityDb, IUserManagementService, IInvitationQueue)
 └─► SharedLibrary   (UI only: shell, nav island, CSS tokens; knows no Core)
```

- **Hexagonal with vertical slices.** `Web/Features/Registration/` mirrors `Core/Features/Registration/`.
- **Hand-rolled use cases**: one `*UseCase` class per file with nested `Request` and `Response` records, no mediator package. Entities live apart in `Core/Entities/`.
- **Database-first.** `schema.sql` owns the schema; the EF mappings are hand-written to match it; there are no migrations.
- **Reused queries** are extension methods on the data port, in `Core/Extensions/`.
- **Static by default**, with one interactive island (the nav) and per-page opt-in.
- **Rate limiting** on the public form's submissions only, per client address, from configuration.
- **Background work** in two `BackgroundService`s: an invitation worker woken by an in-memory queue, and an acceptance check on a fixed schedule. The database row holds the request, so nothing is lost on a restart, and rows are claimed under an optimistic concurrency token so several hosts can run them.

## Where each decision lives

| Decision | Where to read it |
|----------|------------------|
| Dependencies point inward; Core has no provider | `Core/PF.BlazorReference.Core.csproj`, `Tests/Architecture/ArchitectureTests.cs` |
| One `Add<ProjectName>()` per project, enforced by a test | `*/ServiceCollectionExtensions.cs`, `Web/Program.cs`, `Tests/Architecture/ArchitectureTests.cs` |
| Hand-rolled use case, `ErrorOr` results, nested records qualified by their container | `Core/IUseCase.cs`, `Core/Features/Registration/RegisterDeveloperUseCase.cs` |
| Features are use cases; entities are separate | `Core/Features/`, `Core/Entities/`, `Tests/Architecture/ArchitectureTests.cs` |
| Always-valid aggregate, no public setters | `Core/Entities/DeveloperRegistration.cs` |
| Data port and per-call context factory | `Core/IConsentIdentityDb.cs`, `Core/IConsentIdentityDbFactory.cs`, `Infrastructure/ConsentIdentityDbFactory.cs` |
| Reused queries as extensions on the data port | `Core/Extensions/DeveloperRegistrationDbExtensions.cs` |
| Rate limit on the public form's POSTs only: sliding window per client, IPv6 `/64` | `Web/RateLimiting/`, `Web/Features/Registration/RegisterPage.razor`, `Tests/Web/` |
| Provider failure translated to a domain exception | `Infrastructure/ConsentIdentityDbContext.cs` (`SaveChangesAsync`) |
| Database-first schema, hand-written mappings | `Infrastructure/Database/schema.sql`, `Infrastructure/Configurations/` |
| Unique email: pre-check plus index plus race handling | `RegisterDeveloperUseCase.cs`, `schema.sql`, `Tests/Core/RegisterDeveloperUseCaseTests.cs` |
| Audit timestamps from an interceptor using `TimeProvider` | `Core/Entities/IAuditable.cs`, `Infrastructure/Interceptors/AuditTimestampInterceptor.cs` |
| Container and presentation components; presentation binds view models, never Core types | `Web/Features/Registration/*Page.razor(.cs)`, `.../Components/`, `.../ViewModels/`, `Tests/Architecture/ArchitectureTests.cs` |
| Use-case results are contracts: Web never references Core entities | `Core/Features/Registration/ListRegistrationsUseCase.cs` (`Status`, `CanRequestInvitation`), `Tests/Architecture/ArchitectureTests.cs` |
| `.razor` is markup only; code lives in `.razor.cs` | every component, enforced in `Tests/Architecture/ArchitectureTests.cs` |
| Form record with annotations, rules repeated in Core | `Web/Features/Registration/RegisterDeveloperForm.cs` |
| Static SSR by default, one interactive island | `Web/Components/Routes.razor`, `SharedLibrary/NavIsland.razor` |
| Shell takes nav items from the app, as an array | `Web/Components/Layout/MainLayout.razor`, `SharedLibrary/AppShell.razor` |
| Interactive parameters must be serializable, enforced | `SharedLibrary/NavIsland.razor`, `Tests/Architecture/ArchitectureTests.cs` |
| Branding as CSS tokens | `SharedLibrary/wwwroot/pf-tokens.css` |
| Use cases unit-tested with a substituted factory and port, no database | `Tests/Support/FakeConsentIdentityDb.cs`, `Tests/Core/` |
| EF behaviour tested against real SQLite built from `schema.sql` | `Tests/Support/SqliteTestDatabase.cs`, `Tests/Infrastructure/` |
| Antiforgery, redirect-after-post and rate limiting tested over HTTP | `Tests/Web/RegisterEndpointTests.cs` |
| Background work: the row is the record, the channel only a wake-up, sweeps recover lost signals, a scheduled check notices redeemed invitations | `Core/Features/Invitations/`, `Web/BackgroundServices/`, `Infrastructure/Queues/`, `Tests/Web/BackgroundServices/` |
| Workers laid out like features: one folder per worker, a shared base, logic in use cases, enforced by tests | `Web/BackgroundServices/`, `Tests/Architecture/ArchitectureTests.cs` |
| Several hosts: claim a row under a concurrency token before calling Entra | `Core/Entities/IVersioned.cs`, `Core/Extensions/DeveloperRegistrationDbExtensions.cs` (`ClaimAsync`), `Infrastructure/Interceptors/VersionInterceptor.cs`, `Tests/Infrastructure/DeveloperRegistrationDbExtensionsTests.cs` |
| An external SDK behind a port, used by one Infrastructure class and invisible elsewhere | `Core/IUserManagementService.cs`, `Infrastructure/UserManagement/GraphUserManagementService.cs`, `Infrastructure/*.csproj` (`PrivateAssets`), `Tests/Architecture/ArchitectureTests.cs` |

## Adaptations for SQLite, and what is left out

- **`schema.sql` stands in for a SQL Server Database Project.** In a SQL Server setup the `.sqlproj` owns the schema and ships as a DACPAC; the idempotent script and `DatabaseInitializer` are the SQLite substitute. The unique-violation check in `ConsentIdentityDbContext` uses SQLite's error code; SQL Server uses 2601 and 2627.
- **Reads use plain LINQ** and query extension methods, not a specification pattern.
- **Row claims use an optimistic concurrency token**, not database locks. On SQL Server, a worker could instead claim a batch with `UPDATE ... OUTPUT` and `READPAST`, which SQLite cannot express. The `Version` column stands in for `rowversion`, and SQLite runs in WAL mode so two processes can share the file. There is no general outbox.
- **Rate limiting covers only the public form.** It is per app instance and per client address, so it does not stop a distributed volumetric attack (that belongs at the edge), and behind a reverse proxy it needs forwarded headers configured. Pages behind sign-in will be limited per user once authentication exists.
- **No authentication.** The shell takes nav items from the app, but there is no sign-in display or policy. Because `/registrations` is public, the invite button is shown only in Development; it needs an authorization policy before it can be offered anywhere else.
- **No component tests (bUnit).** The toast and the interactive table are checked by hand.
- **Static pages use plain HTML inputs.** MudBlazor inputs need a circuit.
