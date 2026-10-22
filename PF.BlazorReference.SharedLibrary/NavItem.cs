namespace PF.BlazorReference.SharedLibrary;

/// <summary>
/// One entry in the shell's navigation. Passed in by each app, so the shell knows nothing about
/// the app's pages. It must stay JSON-serializable: it crosses the static-to-interactive boundary
/// as a parameter of <see cref="NavIsland"/>.
/// </summary>
public sealed record NavItem(string Title, string Href, string Icon, bool ExactMatch = false);
