# Container and presentation components

Every Blazor component in this app has one of two jobs:

- A **container** decides **how things work**.
- A **presentation** component decides **how things look**.

Keeping the two apart lets the domain change without rewriting the UI, and lets the UI change without touching the domain.

## Where the idea comes from

Dan Abramov described this split for React as **smart** and **dumb** components, and later renamed them **container** and **presentational**. Our docs and tests use his later names, *container* and *presentation*. "Smart component" and "dumb component" mean the same thing.

The idea isn't specific to React. Blazor makes it more useful, because a component may be rendered by a static page or an interactive one, and because we want to test UI decisions without rendering markup.

## The two roles

**Container** (for example `RegistrationsPage`, `RegisterPage`)

- Is the routable page (`@page`).
- Owns the route, the render mode and the page state.
- Injects services and calls Core use cases. It is the only kind of component that injects anything.
- Maps each use-case result into view models.
- Passes view models down and handles the events that come back up.
- Has little markup and almost no styling.

**Presentation** (for example `RegistrationTable`, `RegistrationForm`, `AppShell`)

- Takes `[Parameter]` values in and raises `EventCallback` out.
- Injects nothing.
- Binds view models only. It never sees a use case, an entity or a domain rule.
- May use UI libraries such as MudBlazor.
- Keeps only UI state: a search box, an open drawer, a selected tab.
- Lives in `Features/<Feature>/Components/` or in the SharedLibrary project.

## How data moves: down as view models, up as events

```
Core use case                    Container                    View model                Presentation
ListRegistrationsUseCase  --->   RegistrationsPage    --->    RegistrationRow    --->   RegistrationTable
  (returns Item)                 RegistrationRow.From(...)                                 |
                                       ^                                                   |
                                       |               OnInvite(id)  (EventCallback)       |
                                       +---------------------------------------------------+
                                       |
                                       v
                                 RequestInvitationUseCase (Core)
```

The presentation component never talks to Core. Everything it shows arrives already decided, and everything the user does goes back to the container as an event.

The **view model** sits in the middle. It's a plain record holding every label, color, hint and enabled flag the component needs. A static `From(...)` method builds it from the use case's result:

```csharp
// Features/Registration/ViewModels/RegistrationRow.cs
public sealed record RegistrationRow(
    Guid Id, string Name, string Email, DateTime CreatedAtUtc,
    string StatusLabel, Color StatusColor, string? StatusHint,
    bool CanInvite, string InviteLabel, bool IsSendingInvitation)
{
    public static RegistrationRow From(ListRegistrationsUseCase.Item item) => ...
}
```

The container loads the data, maps it and passes it down:

```csharp
// RegistrationsPage.razor.cs (container)
var result = await ListRegistrations.Handle(new ListRegistrationsUseCase.Request(), ct);
state = state with { Rows = RegistrationRow.From(result.Value.Items) };
```

```razor
@* RegistrationsPage.razor *@
<RegistrationTable Rows="state.Rows" ShowInvite="CanInvite"
                   CurrentRecordProcessingId="currentRecordProcessingId"
                   OnInvite="RequestInvitationAsync" />
```

## Where each decision is made: the invite button

The registrations table shows an invite button on each row. Three decisions go into it, each made in a different place:

- **Can this row be invited?** A business rule, decided in Core by the entity. It reaches the UI as `CanRequestInvitation` on the use case's result.
- **What does the button say?** A presentation decision ("Invite" or "Retry invite"), made in the view model as `InviteLabel`.
- **Is a request for this row in flight?** Page state, held by the container and passed down as `CurrentRecordProcessingId`.

The presentation component only binds the results:

```razor
<MudButton Disabled="@(!context.Item.CanInvite || context.Item.Id == CurrentRecordProcessingId)"
           OnClick="@(() => OnInvite.InvokeAsync(context.Item.Id))">
    @context.Item.InviteLabel
</MudButton>
```

## Why we do it

1. **Domain changes stop at the use case.** Use cases return their own contract types (`ListRegistrationsUseCase.Item`, with its own `Status` enum), never an entity. Containers map those into view models. Renaming a status or adding a column to an entity changes Core and the mapping, and presentation components aren't affected.
2. **UI decisions are tested as plain data.** Labels, colors and hints live in view models, so a unit test can check "a failed row says Retry invite and shows the reason" without rendering a component or adding bUnit. See `Tests/Web/Features/Registration/RegistrationRowTests.cs`.
3. **Components work on static and interactive pages.** In Blazor, the page that declares `@rendermode` decides where a component runs. A presentation component that only takes parameters and injects nothing can be rendered by a static page (`RegistrationForm` on `/register`) or an interactive one (`RegistrationTable` on `/registrations`) without changes. The container owns the render mode, so that choice sits in one place.
4. **Reuse, and one place to change a look.** The shell (`AppShell`, `NavIsland`) lives in a separate Razor class library and knows nothing about this app. Because presentation components carry no app logic, they can move to a shared library and be reused.
5. **Easier to read.** A container tells you what the page does, and a presentation component tells you what it looks like. With markup in `.razor` and code in `.razor.cs`, you rarely need to read both to answer a question.

## The rules, and how they're enforced

Most of these rules are checked by architecture tests in `Tests/Architecture/ArchitectureTests.cs`. Breaking one fails the test run with a message that names the offending type or file.

- `Presentation_components_inject_nothing`: no `[Inject]` in a presentation component. Only containers inject services.
- `Presentation_components_depend_on_nothing_in_Core`: components under `Features/<Feature>/Components/` reference no Core type at all (no use case, no contract, no entity).
- `SharedLibrary_references_neither_Core_nor_Infrastructure`: the shared UI library knows nothing about any app's domain.
- `Web_never_references_Core_entities`: nothing in Web (pages, view models, workers) touches `Core.Entities`. Use-case contracts are the only way in.
- `Interactive_components_take_only_parameters_that_can_cross_the_render_mode_boundary`: a component with `@rendermode` takes only serializable parameters (no interfaces, delegates or `EventCallback`).
- `Razor_files_hold_markup_only`: no `@code` or `@functions` block. C# goes in the `.razor.cs` partial class.

## Folder layout for a feature

```
Web/Features/Registration/
  RegistrationsPage.razor        container: route, render mode, markup that places children
  RegistrationsPage.razor.cs     container: [Inject] use cases, state, handlers, mapping
  Components/
    RegistrationTable.razor      presentation: markup only
    RegistrationTable.razor.cs   presentation: [Parameter]s, EventCallbacks, UI state
  ViewModels/
    RegistrationRow.cs           what the table shows, already decided

Core/Features/Registration/
  ListRegistrationsUseCase.cs    the contract the container maps from
```

Background workers follow the same idea. A worker in `Web/BackgroundServices/<Worker>/` is the background counterpart of a container: it decides *when* work runs, and calls Core use cases for *what* happens.

## Common questions

**Where does display text like "Sending invite" go?**
In the view model. Wording, colors and icons are presentation decisions, so they're made in `From(...)` and tested there.

**Where does a business rule like "can this be invited?" go?**
In Core, on the entity or in the use case. It reaches the UI as a flag on the use case's result (`Item.CanRequestInvitation`). The view model copies it and never re-derives it.

**A view model references a Core use case. Isn't that coupling?**
It references the use case's *contract*, which is meant to be consumed, and never an entity. The view model is the one place where Core's shape is translated into the UI's shape, so presentation components don't need to know either.

**Can a presentation component have state?**
Yes, if the state only changes what the user sees: a search filter, an expanded row, an open drawer. Application data, or anything that gets saved, belongs in the container.

**Does every page need a presentation child?**
No. Abramov later warned against splitting components dogmatically. A static page with a heading and a paragraph (`Home`, `NotFound`) stays as it is. Pull out a presentation component when a piece of UI has its own decisions to test, is reused, or has to work under more than one render mode.

**Can a presentation component call a use case "just this once"?**
No, and the tests will stop it. Raise an `EventCallback` and let the container call the use case. That keeps every use-case call, and its error handling, in one place per page.

## Further reading

- Categorizing Components Into Smart & Dumb Components (DigitalOcean): https://www.digitalocean.com/community/tutorials/react-smart-dumb-components
- Presentational and Container Components (Dan Abramov): https://medium.com/@dan_abramov/smart-and-dumb-components-7ca2f9a7c7d0
- The repo's full conventions are in `AGENTS.md`, under "Blazor pages" and "Background services".
