using Microsoft.AspNetCore.Components;

namespace PF.BlazorReference.SharedLibrary;

/// <summary>Presentation: the application shell. Parameters in, nothing injected.</summary>
public partial class AppShell
{
    [Parameter, EditorRequired] public string AppTitle { get; set; } = "";
    // An array, because NavIsland needs one (see the comment there).
    [Parameter, EditorRequired] public NavItem[] NavItems { get; set; } = [];
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
