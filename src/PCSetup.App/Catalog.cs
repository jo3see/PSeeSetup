namespace PCSetup;

public static class Catalog
{
    public static readonly IReadOnlyList<AppDefinition> Applications =
    [
        App("snapdragoncontrolpanel", "Snapdragon Control Panel", "System utilities", "", InstallContext.User, false,
            "For Snapdragon ARM64 PCs on Windows 11 24H2 or later. Includes ARM64 .NET 9 Desktop Runtime 9.0.21 when needed. Uses Qualcomm's official MSIX; accept its EULA on first launch."),
        App("chrome", "Google Chrome", "Browsers", "Google.Chrome", InstallContext.Machine, true, "Fast, managed-compatible web browser."),
        App("brave", "Brave Web Browser", "Browsers", "Brave.Brave", InstallContext.Machine, true, "Privacy-focused Chromium web browser."),
        App("tor", "Tor Browser", "Browsers", "TorProject.TorBrowser", InstallContext.User, true, "Privacy-focused browser installed for the signed-in user.",
            detectionPaths: [@"%USERPROFILE%\Desktop\Tor Browser\Browser\firefox.exe", @"%LocalAppData%\Tor Browser\Browser\firefox.exe",
                @"%LocalAppData%\Programs\Tor Browser\Browser\firefox.exe"], requireKnownInstallPath: true),

        App("teams", "Microsoft Teams", "Work & development", "Microsoft.Teams", InstallContext.Machine, true, "New Microsoft Teams client.",
            new("https://go.microsoft.com/fwlink/?linkid=2243204", "teamsbootstrapper.exe", "-p", ["Microsoft Corporation"])),
        App("slack", "Slack", "Work & development", "SlackTechnologies.Slack", InstallContext.User, true, "Desktop collaboration client installed for the signed-in user."),
        App("claude", "Claude", "Work & development", "Anthropic.Claude", InstallContext.User, true, "Anthropic desktop application installed for the signed-in user."),
        App("discord", "Discord", "Work & development", "Discord.Discord", InstallContext.User, true, "Voice, video, and text community client installed for the signed-in user."),
        App("bluebubbles", "BlueBubbles", "Work & development", "9P3XF8KJ0LSM", InstallContext.User, true,
            "Windows iMessage client; requires a BlueBubbles server running on a Mac.",
            detectionName: "BlueBubbles",
            detectionPaths: [@"%LocalAppData%\Programs\BlueBubbles\BlueBubbles.exe",
                @"%LocalAppData%\Programs\bluebubbles\bluebubbles.exe", @"%ProgramFiles%\BlueBubbles\BlueBubbles.exe"],
            wingetSource: "msstore"),
        App("vscode", "Visual Studio Code", "Work & development", "Microsoft.VisualStudioCode", InstallContext.Machine, true, "Code editor."),
        App("postman", "Postman", "Work & development", "Postman.Postman", InstallContext.User, true, "API development client."),
        App("powerautomate", "Power Automate for desktop", "Work & development", "Microsoft.PowerAutomateDesktop", InstallContext.Machine, true, "Desktop automation tooling."),
        App("powerbi", "Power BI Desktop", "Work & development", "Microsoft.PowerBI", InstallContext.Machine, true, "Business intelligence authoring."),
        App("powershell", "PowerShell", "Work & development", "Microsoft.PowerShell", InstallContext.Machine, true,
            "Installs or upgrades the latest stable PowerShell.",
            detectionPaths: [@"%ProgramFiles%\PowerShell\7\pwsh.exe",
                @"%LocalAppData%\Microsoft\WindowsApps\pwsh.exe",
                @"%LocalAppData%\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe"],
            upgradeIfInstalled: true, requireKnownInstallPath: true,
            validationArguments: ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "exit 0"]),
        App("powertoys", "Microsoft PowerToys", "Work & development", "Microsoft.PowerToys", InstallContext.Machine, true, "Windows productivity utilities."),

        App("ea", "EA app", "Gaming", "ElectronicArts.EADesktop", InstallContext.Machine, true, "Electronic Arts game launcher."),
        App("epic", "Epic Games Launcher", "Gaming", "EpicGames.EpicGamesLauncher", InstallContext.Machine, true, "Epic game launcher."),
        App("steam", "Steam", "Gaming", "Valve.Steam", InstallContext.Machine, true, "Steam game platform."),
        App("ubisoft", "Ubisoft Connect", "Gaming", "Ubisoft.Connect", InstallContext.Machine, true, "Ubisoft game launcher."),
        App("modrinth", "Modrinth", "Gaming", "Modrinth.ModrinthApp", InstallContext.User, true, "Minecraft mod and launcher application installed for the signed-in user.", detectionName: "Modrinth App"),

        App("equalizerapo", "Equalizer APO", "Media", "EqualizerAPO.EqualizerAPO", InstallContext.Machine, true, "System-wide audio equalizer; its configurator opens at the end.",
            new("https://downloads.sourceforge.net/project/equalizerapo/1.4.2/EqualizerAPO-x64-1.4.2.exe", "EqualizerAPO-x64-1.4.2.exe", "/S",
                ["Jonas Thedering", "Open Source Developer"],
                ExpectedSha256: "7403BE7427BBE1936A40DDED082829B6E217FC4F5990FEE5CBA501F0AE055AFA",
                AllowPinnedHashWithoutSignature: true),
            "Equalizer APO",
            [@"%ProgramFiles%\EqualizerAPO\Editor.exe", @"%ProgramFiles%\EqualizerAPO\Configurator.exe",
                @"%ProgramFiles%\EqualizerAPO\DeviceSelector.exe"], requireKnownInstallPath: true),
        App("ytdlp", "yt-dlp", "Media", "yt-dlp.yt-dlp", InstallContext.Machine, true, "Command-line media downloader."),
        App("ffmpeg", "FFmpeg", "Media", "Gyan.FFmpeg", InstallContext.Machine, true, "Full FFmpeg media tools."),
        App("audacity", "Audacity", "Media", "Audacity.Audacity", InstallContext.Machine, true, "Audio editor."),
        App("obs", "OBS Studio", "Media", "OBSProject.OBSStudio", InstallContext.Machine, true, "Recording and streaming studio. ARM portable setup is managed automatically."),
        App("vlc", "VLC", "Media", "VideoLAN.VLC", InstallContext.Machine, true, "Media player."),
        App("spotify", "Spotify", "Media", "Spotify.Spotify", InstallContext.User, true, "Installed in the normal signed-in user context before UAC is disabled."),
        App("paintdotnet", "Paint.NET", "Media", "dotPDN.PaintDotNet", InstallContext.Machine, true, "Image editor."),
        App("klite", "K-Lite Codec Pack Standard", "Media", "CodecGuide.K-LiteCodecPack.Standard", InstallContext.Machine, true, "Codec collection and Media Player Classic."),

        App("advancedip", "Advanced IP Scanner", "Network & remote", "Famatech.AdvancedIPScanner", InstallContext.Machine, true, "Local network scanner."),
        App("wireshark", "Wireshark", "Network & remote", "WiresharkFoundation.Wireshark", InstallContext.Machine, true, "Packet capture and analysis."),
        App("putty", "PuTTY", "Network & remote", "PuTTY.PuTTY", InstallContext.Machine, true, "SSH and serial client."),
        App("parsec", "Parsec", "Network & remote", "Parsec.Parsec", InstallContext.Machine, true, "Low-latency remote access.",
            new("https://builds.parsec.app/package/parsec-windows.exe", "parsec-windows.exe", "/silent /percomputer /norun /nocleanuser",
                ["Parsec Cloud, Inc.", "Unity Technologies"]),
            "Parsec",
            [@"%ProgramFiles%\Parsec\parsecd.exe", @"%ProgramFiles%\Parsec\parsec.exe",
                @"%LocalAppData%\Programs\Parsec\parsecd.exe"]),
        App("tailscale", "Tailscale", "Network & remote", "Tailscale.Tailscale", InstallContext.Machine, true, "Private mesh networking."),
        App("teamviewer", "TeamViewer", "Network & remote", "TeamViewer.TeamViewer", InstallContext.Machine, true, "Remote support client.",
            detectionPaths: [@"%ProgramFiles%\TeamViewer\TeamViewer.exe", @"%ProgramW6432%\TeamViewer\TeamViewer.exe"],
            requireKnownInstallPath: true),
        App("spacedesk", "SpaceDesk (Driver)", "Network & remote", "Datronicsoft.SpacedeskDriver.Server", InstallContext.Machine, true, "Primary-PC display driver for spacedesk viewers.", detectionName: "spacedesk Windows DRIVER"),

        App("balenaetcher", "Balena Etcher", "Utilities", "Balena.Etcher", InstallContext.User, true, "SD card and USB image writer.", detectionName: "balenaEtcher"),
        App("jdownloader2", "JDownloader 2 (ad-free)", "Utilities", "AppWork.JDownloader", InstallContext.User, true, "Official JDownloader 2 installer without bundled offers.",
            new("https://installer.jdownloader.org/JD2Setup_x64WithoutOffers.exe", "JD2Setup_x64WithoutOffers.exe", "-q", ["AppWork GmbH"])),
        App("orcaslicer", "OrcaSlicer", "Utilities", "SoftFever.OrcaSlicer", InstallContext.Machine, true, "3D-printer slicer."),
        App("nvcleanstall", "NVCleanstall", "Utilities", "TechPowerUp.NVCleanstall", InstallContext.Machine, true, "NVIDIA driver customization tool."),
        App("7zip", "7-Zip", "Utilities", "7zip.7zip", InstallContext.Machine, true, "Archive utility."),
        App("afterburner", "MSI Afterburner", "Utilities", "Guru3D.Afterburner", InstallContext.Machine, true, "GPU monitoring and tuning."),
        App("transmission", "Transmission", "Utilities", "Transmission.Transmission", InstallContext.Machine, true, "BitTorrent client."),
        App("wiztree", "WizTree", "Utilities", "AntibodySoftware.WizTree", InstallContext.Machine, true, "Disk space analyzer."),
        App("rpiimager", "Raspberry Pi Imager", "Utilities", "RaspberryPiFoundation.RaspberryPiImager", InstallContext.Machine, true, "Raspberry Pi image writer.",
            detectionPaths: [@"%ProgramFiles%\Raspberry Pi Ltd\Imager\rpi-imager.exe",
                @"%ProgramFiles%\Raspberry Pi Imager\rpi-imager.exe"]),
        App("logioptionsplus", "Logi Options+", "Utilities", "Logitech.OptionsPlus", InstallContext.Machine, true, "Logitech mouse, keyboard, and workflow customization."),
        App("ultraiso", "UltraISO", "Utilities", "EZBSystems.UltraISO", InstallContext.Machine, true, "ISO image creation, editing, conversion, and writing utility.", detectionName: "UltraISO Premium"),
        App("unraidusb", "Unraid USB Creator", "Utilities", "LimeTechnology.UnraidUSBCreator", InstallContext.Machine, true, "Creates bootable USB media for Unraid OS.",
            new("https://github.com/unraid/usb-creator-next/releases/download/v2.0.11.1-unraid.0/unraid-usb-creator-v2.0.11.1-unraid.0.exe",
                "unraid-usb-creator-v2.0.11.1-unraid.0.exe", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                ["Lime Technology, Inc."], "7E29F1F907488E5FEDC6044E1F2256ABC55250B21BAFEDE28E78AF02EBEDC7C4"),
            detectionPaths: [@"%ProgramFiles%\Lime Technology, Inc.\Unraid USB Creator\unraid-usb-creator.exe",
                @"%ProgramW6432%\Lime Technology, Inc.\Unraid USB Creator\unraid-usb-creator.exe"]),

        App("rainmeter", "Rainmeter", "Appearance", "Rainmeter.Rainmeter", InstallContext.Machine, true, "Desktop customization platform."),
        App("translucenttb", "TranslucentTB", "Appearance", "CharlesMilette.TranslucentTB", InstallContext.User, true, "Transparent taskbar utility installed for the signed-in user.",
            new("https://github.com/TranslucentTB/TranslucentTB/releases/latest/download/TranslucentTB-portable-x64.zip", "TranslucentTB-portable-x64.zip", "", ["Charles Milette"],
                PortableZip: true, PortableExecutable: "TranslucentTB.exe", InstallDirectoryName: "TranslucentTB")),

        App("dotnet10", ".NET Desktop Runtime 10", "Runtimes", "Microsoft.DotNet.DesktopRuntime.10", InstallContext.Machine, true, "Current .NET desktop runtime."),
        App("dotnet9", ".NET Desktop Runtime 9", "Runtimes", "Microsoft.DotNet.DesktopRuntime.9", InstallContext.Machine, false, "Optional compatibility runtime."),
        App("dotnet8", ".NET Desktop Runtime 8", "Runtimes", "Microsoft.DotNet.DesktopRuntime.8", InstallContext.Machine, false, "Optional long-term-support compatibility runtime.")
    ];

    public static readonly IReadOnlyList<TweakDefinition> Tweaks =
    [
        Tweak("telemetry", "Reduce Windows diagnostic data", "Privacy", true, "Sets diagnostic data to the minimum allowed by the installed Windows edition."),
        Tweak("activity_history", "Disable Activity History", "Privacy", true, "Stops publishing and uploading user activities."),
        Tweak("powershell_telemetry", "Disable PowerShell telemetry", "Privacy", true, "Uses the supported POWERSHELL_TELEMETRY_OPTOUT environment variable."),

        Tweak("folder_discovery", "Disable automatic folder-type discovery", "File Explorer", true, "Keeps Explorer from changing folder templates automatically."),
        Tweak("classic_menu", "Use the classic context menu", "File Explorer", true, "Restores the full right-click menu.", true),
        Tweak("remove_home", "Remove Home from Explorer", "File Explorer", true, "Hides Home from the navigation pane.", true),
        Tweak("remove_gallery", "Remove Gallery from Explorer", "File Explorer", true, "Hides Gallery using the correct namespace registration.", true),
        Tweak("hide_extensions", "Hide known file extensions", "File Explorer", true, "Keeps the original preference from the setup script.", true),

        Tweak("disable_gamedvr", "Disable Game DVR", "Windows behavior", true, "Turns off background game capture."),
        Tweak("disable_hibernation", "Disable hibernation and Fast Startup", "Windows behavior", true, "Prevents hibernation from consuming disk and battery."),
        Tweak("power_button_sleep", "Set power button to Sleep", "Windows behavior", true, "Sets both AC and battery power-button actions to Sleep."),
        Tweak("disable_storage_sense", "Disable Storage Sense", "Windows behavior", true, "Stops automatic storage cleanup."),
        Tweak("taskbar_end_task", "Enable End task on taskbar", "Windows behavior", true, "Adds End task to supported taskbar app menus."),
        Tweak("terminal_ps7", "Make PowerShell 7 the Windows Terminal default", "Windows behavior", true, "Makes new Windows Terminal tabs open PowerShell 7 while retaining legacy Windows PowerShell for compatibility."),
        Tweak("disable_web_search", "Disable web suggestions in Start", "Windows behavior", true, "Keeps Start search local."),
        Tweak("disable_start_recommendations", "Disable Start recommendations", "Windows behavior", true, "Reduces suggested content in Start."),
        Tweak("disable_snap", "Disable Snap layouts and suggestions", "Windows behavior", true, "Turns off Snap windows, flyouts, and suggestions."),
        Tweak("disable_dynamic_lighting", "Disable Dynamic Lighting", "Windows behavior", true, "Turns off Windows RGB control."),
        Tweak("disable_sticky_hotkey", "Disable the Sticky Keys shortcut", "Windows behavior", true, "Prevents the five-Shift shortcut while retaining accessibility settings."),
        Tweak("hide_task_view", "Hide Task View", "Windows behavior", true, "Removes the Task View taskbar button.", true),
        Tweak("detailed_bsod", "Show detailed stop-screen information", "Windows behavior", true, "Displays diagnostic parameters on a blue screen."),
        Tweak("disk_cleanup", "Run monitored Disk Cleanup", "Windows behavior", true, "Runs Windows Disk Cleanup non-interactively and waits for its result."),

        Tweak("edge_lean", "Keep Microsoft Edge lean", "Microsoft Edge", true, "Disables background/marketing extras while preserving updates, security, profiles, sync, WebView2, PWAs, PDFs, extensions, and work sign-in.")
    ];

    public static readonly IReadOnlyList<FeatureDefinition> Features =
    [
        new("hyperv", "Hyper-V", "Microsoft-Hyper-V-All", true, "Virtualization platform; requires supported hardware and edition."),
        new("sandbox", "Windows Sandbox", "Containers-DisposableClientVM", true, "Disposable Windows environment; requires supported hardware and edition."),
        new("nfs", "Client for NFS", "ServicesForNFS-ClientOnly", true, "Network File System client."),
        new("netfx3", ".NET Framework 3.5", "NetFx3", true, "Legacy .NET Framework compatibility feature.")
    ];

    public static AppDefinition AppByKey(string key) =>
        Applications.First(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static TweakDefinition TweakByKey(string key) =>
        Tweaks.First(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static FeatureDefinition FeatureByKey(string key) =>
        Features.First(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static AppDefinition App(string key, string name, string category, string id, InstallContext context,
        bool selected, string description, VendorFallback? fallback = null, string? detectionName = null,
        string[]? detectionPaths = null, bool upgradeIfInstalled = false, string? wingetSource = null,
        bool requireKnownInstallPath = false, string[]? validationArguments = null) =>
        new(key, name, category, id, context, selected, description, fallback, detectionName, detectionPaths,
            upgradeIfInstalled, wingetSource, requireKnownInstallPath, validationArguments);

    private static TweakDefinition Tweak(string key, string name, string category, bool selected,
        string description, bool restartExplorer = false) =>
        new(key, name, category, selected, description, restartExplorer);
}
