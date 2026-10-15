namespace PCSetup;

public sealed record UninstallDefinition(string Key, string Name, string Category, string[] PackageNames,
    string[] DisplayNames, string[] Publishers, string? FeatureName = null, bool StandaloneRemoval = true);

public static class UninstallCatalog
{
    private static UninstallDefinition MicrosoftApp(string key, string name, params string[] packages) =>
        new(key, name, "Microsoft", packages, [], ["Microsoft"]);
    private static UninstallDefinition AsusApp(string key, string name, string[] packages, params string[] names) =>
        new(key, name, "ASUS", packages, names, ["ASUS", "ASUSTeK"]);

    public static readonly IReadOnlyList<UninstallDefinition> Applications =
    [
        AsusApp("glidex", "ASUS GlideX", ["B9ECED6F.GlideX"], "ASUS GlideX", "GlideX"),
        new("deno", "Deno", "Development", [], ["Deno"], ["Deno Land"]),
        AsusApp("glidexservice", "GlideX Service Installer", [], "GlideX Service Installer"),
        AsusApp("storycubeservice", "StoryCube Service Installer", [], "StoryCube Service Installer"),
        MicrosoftApp("family", "Family", "MicrosoftCorporationII.MicrosoftFamily"),
        MicrosoftApp("feedback", "Feedback Hub", "Microsoft.WindowsFeedbackHub"),
        MicrosoftApp("gamebar", "Game Bar", "Microsoft.XboxGamingOverlay", "Microsoft.XboxGameOverlay"),
        MicrosoftApp("gethelp", "Get Help", "Microsoft.GetHelp"),
        MicrosoftApp("getstarted", "Get Started", "Microsoft.Getstarted"),
        new("clipchamp", "Microsoft Clipchamp", "Microsoft", ["Clipchamp.Clipchamp"], [], ["Microsoft", "Clipchamp"]),
        new("copilotdesktop", "Copilot", "Microsoft", [], ["Copilot", "Microsoft Copilot"], ["Microsoft"]),
        MicrosoftApp("copilotstore", "Microsoft Copilot", "Microsoft.Copilot"),
        MicrosoftApp("todo", "Microsoft To Do", "Microsoft.Todos"),
        MicrosoftApp("news", "News", "Microsoft.BingNews"),
        MicrosoftApp("paint", "Paint", "Microsoft.Paint"),
        MicrosoftApp("phonelink", "Phone Link", "Microsoft.YourPhone"),
        MicrosoftApp("quickassist", "Quick Assist", "MicrosoftCorporationII.QuickAssist"),
        new("recall", "Recall", "Windows feature", [], [], ["Microsoft"], "Recall"),
        AsusApp("screenxpert", "ScreenXpert", ["B9ECED6F.ScreenXpert"], "ScreenXpert", "ASUS ScreenXpert"),
        MicrosoftApp("solitaire", "Solitaire", "Microsoft.MicrosoftSolitaireCollection"),
        MicrosoftApp("stickynotes", "Sticky Notes", "Microsoft.MicrosoftStickyNotes"),
        new("stickynotesnew", "Sticky Notes (new)", "Microsoft", [], ["Sticky Notes (new)"], ["Microsoft"], StandaloneRemoval: false),
        AsusApp("storycube", "StoryCube", ["B9ECED6F.ASUSStoryCube", "B9ECED6F.StoryCube"], "StoryCube", "ASUS StoryCube"),
        MicrosoftApp("xbox", "Xbox", "Microsoft.GamingApp", "Microsoft.XboxApp")
    ];

    public static UninstallDefinition ByKey(string key) => Applications.First(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
