using Microsoft.AspNetCore.Components;

namespace PF.BlazorReference.SharedLibrary;

/// <summary>Presentation, and the layout's one interactive island. Parameters in, nothing injected.</summary>
public partial class NavIsland
{
    private bool drawerOpen = true;

    [Parameter, EditorRequired] public string AppTitle { get; set; } = "";
    // An array, not IReadOnlyList<T>: this parameter is serialized across the static-to-interactive
    // boundary using the value's runtime type, and a collection expression assigned to an interface
    // becomes a hidden compiler type that cannot be deserialized. The array type makes the compiler
    // reject that. An architecture test guards every interactive component's parameters.
    [Parameter, EditorRequired] public NavItem[] NavItems { get; set; } = [];

    private void ToggleDrawer() => drawerOpen = !drawerOpen;
}
