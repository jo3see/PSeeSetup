# PSeeSetup 1.2.7 prerelease

Adds an optional Snapdragon Control Panel installation to the Applications tab.

## Behavior

- Unchecked by default. Eligible only on Windows ARM64 with a detected Snapdragon/Qualcomm processor and Windows 11 build 26100 (24H2) or later. Detection uses OS architecture and the machine processor registry, including when the app process runs under x64 emulation.
- Uses Qualcomm Control Panel 2026.3.0.0, installed for the signed-in user. An existing healthy ARM64 package from the expected publisher at this version or newer is retained.
- Installs Microsoft .NET 9 Desktop Runtime 9.0.21 ARM64 first if a stable 9.0.21 or newer 9.0 patch is not present in the native dotnet installation. x64/x86 runtimes, the SDK alone, .NET Core alone and .NET 10 do not satisfy this dependency.
- A runtime reboot request is recorded separately and setup resumes in the user-app phase before attempting the control panel. A failed or unverified runtime blocks the control panel installation. Unrelated optional work can continue.
- Downloads use HTTPS. The Qualcomm ZIP is pinned by SHA256; only its expected MSIX is extracted. Windows validates the Qualcomm signature, and the manifest must match the expected name, publisher, version and ARM64 architecture before deployment.
- Checks the two Microsoft-signed ARM64 Visual C++ framework dependencies declared by the MSIX. If either is absent, setup reports the missing framework and asks you to update Store apps/App Installer before retrying.
- Qualcomm's first-launch EULA remains for the user to accept. No graphics-driver update is performed by this option.

## Download

Extract the entire PSeeSetup-1.2.7.zip and launch PSeeSetup.exe. The launcher selects the native application. Start a fresh run when changing versions; saved 1.2.6 runs are not resumed as 1.2.7.

## Authoritative references

- [Qualcomm Snapdragon Control Panel catalog and official download](https://softwarecenter.qualcomm.com/catalog/item/SnapdragonControlPanel), including its bundled release notes, MSIX manifest and install instructions.
- [Microsoft .NET 9.0.21 Desktop Runtime ARM64](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-9.0.21-windows-arm64-installer?cid=getdotnetcore).

## Validation

- 99/99 checks passed in a native ARM64 process and 99/99 in an x64 process under ARM emulation. Includes opt-in production hash/signature validation of the real Qualcomm and Microsoft downloads. Sanitized results are in docs/testing.
- Release builds succeeded for the native ARM64 app, x64 app and architecture-selecting launcher using .NET SDK 10.0.402. The Applications tab was visually inspected with synthetic inventory data.
- Installer execution is mocked in automated checks. The new live Control Panel installation and real dependency-reboot path have not been executed by this update; the tests did not run setup or change Windows preferences.
- A GitHub Actions release job runs the standard suite on Windows x64, builds both architectures and publishes the ZIP plus SHA256 file. Existing releases are never replaced. The job runs on a version change on main or can be started manually.
