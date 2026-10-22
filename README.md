# Blazor reference app

A small, runnable Blazor Server app (.NET 10) that shows one design end to end, against a local SQLite database. It has one use case: register a developer with a name and email, with validation and a unique-email check.

It exists to prove that the design's conventions compile, hold together and can be tested, and to give a working example to copy from. The code stays small, and comments explain only the non-obvious parts.

## Run it

Requires the .NET 10 SDK.

```bash
dotnet run --project PF.BlazorReference.Web      # http://localhost:5027
dotnet test PF.BlazorReference.slnx
```

On first run the app creates `App_Data/reference.db` from `schema.sql`. In Development it also loads four sample rows from `seed.sql`. Run the app from the repo root or the Web project folder; the database path is relative to the working directory.

## What to look at

| Page | Render mode | Shows |
|------|-------------|-------|
| `/` | Static SSR | The shell with no circuit open for the page |
| `/register` | Static SSR | `EditForm` posting to the server: annotation messages, domain errors on the right field, antiforgery, redirect after success |
| `/registrations` | `InteractiveServer` | A sortable, filterable MudBlazor table, and a toast after a redirect from `/register` |

## Architecture in one screen

```
Web ──────────► Core ◄────────── Infrastructure
 │               ▲  use cases, aggregate, ports (IConsentIdentityDb)
 └─► SharedLibrary   (UI only: shell, nav island, CSS tokens; knows no Core)
```

- **Hexagonal with vertical slices.** `Web/Features/Registration/` mirrors `Core/Features/Registration/`.
- **Hand-rolled use cases**: one `*UseCase` class per file with nested `Request` and `Response` records, no mediator package. Entities live apart in `Core/Entities/`.
- **Database-first.** `schema.sql` owns the schema; the EF mappings are hand-written to match it; there are no migrations.
- **Reused queries** are extension methods on the data port, in `Core/Extensions/`.
- **Static by default**, with one interactive island (the nav) and per-page opt-in.
- **Rate limiting** on the public form's submissions only, per client address, from configuration.

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
| Container and presentation components | `Web/Features/Registration/*Page.razor(.cs)`, `.../Components/` |
| Form record with annotations, rules repeated in Core | `Web/Features/Registration/RegisterDeveloperForm.cs` |
| Static SSR by default, one interactive island | `Web/Components/Routes.razor`, `SharedLibrary/NavIsland.razor` |
| Shell takes nav items from the app, as an array | `Web/Components/Layout/MainLayout.razor`, `SharedLibrary/AppShell.razor` |
| Interactive parameters must be serializable, enforced | `SharedLibrary/NavIsland.razor`, `Tests/Architecture/ArchitectureTests.cs` |
| Branding as CSS tokens | `SharedLibrary/wwwroot/pf-tokens.css` |
| Use cases unit-tested with a substituted factory and port, no database | `Tests/Support/FakeConsentIdentityDb.cs`, `Tests/Core/` |
| EF behaviour tested against real SQLite built from `schema.sql` | `Tests/Support/SqliteTestDatabase.cs`, `Tests/Infrastructure/` |
| Antiforgery, redirect-after-post and rate limiting tested over HTTP | `Tests/Web/RegisterEndpointTests.cs` |

## Adaptations for SQLite, and what is left out

- **`schema.sql` stands in for a SQL Server Database Project.** In a SQL Server setup the `.sqlproj` owns the schema and ships as a DACPAC; the idempotent script and `DatabaseInitializer` are the SQLite substitute. The unique-violation check in `ConsentIdentityDbContext` uses SQLite's error code; SQL Server uses 2601 and 2627.
- **Reads use plain LINQ** and query extension methods, not a specification pattern.
- **No outbox or background processor.** A processor that claims rows would behave differently on SQLite (no `READPAST`), which would teach the wrong thing about concurrency.
- **Rate limiting covers only the public form.** It is per app instance and per client address, so it does not stop a distributed volumetric attack (that belongs at the edge), and behind a reverse proxy it needs forwarded headers configured. Pages behind sign-in will be limited per user once authentication exists.
- **No authentication.** The shell takes nav items from the app, but there is no sign-in display or policy.
- **No component tests (bUnit).** The toast and the interactive table are checked by hand.
- **Static pages use plain HTML inputs.** MudBlazor inputs need a circuit.
