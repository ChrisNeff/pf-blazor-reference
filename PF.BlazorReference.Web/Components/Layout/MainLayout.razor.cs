using MudBlazor;
using PF.BlazorReference.SharedLibrary;

namespace PF.BlazorReference.Web.Components.Layout;

public partial class MainLayout
{
    // NavItem[] is what the shell's parameters require; see the comment on NavIsland.NavItems.
    private static readonly NavItem[] NavItems =
    [
        new("Home", "/", Icons.Material.Filled.Home, ExactMatch: true),
        new("Register", "/register", Icons.Material.Filled.PersonAdd),
        new("Registrations", "/registrations", Icons.Material.Filled.List),
    ];
}
