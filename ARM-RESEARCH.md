> Historical research for internal version 1.2.3. See README.md and CHANGELOG.md for current 1.2.6 behavior.

# PC Setup 1.2.3 — ARM research and 1.2.2 findings

Reviewed October 13, 2026. Vendor pages, current Microsoft WinGet manifests and official release assets were checked. ARM64 builds for Linux or macOS are not treated as Windows ARM64 builds. An architecture entry describes the installer package; an x86 wrapper can contain ARM64 or x64 application binaries.

## Selection behavior

Every catalog app is selectable. On Snapdragon, ordinary applications query ARM64 first, then x64, then x86. Intel/AMD Windows queries x64 then x86, never ARM64. BlueBubbles stays on its official Microsoft Store product; Windows selects an applicable package. Existing installations remain unchanged, except the existing PowerShell upgrade rule.

Native kernel drivers cannot use Intel emulation. Driver/shell/setup dependencies (Equalizer APO, spacedesk, Tailscale, PowerToys, TranslucentTB, PowerShell and native .NET runtimes) retain explicit ARM64 packages. If those packages are unavailable, setup reports the failure rather than claiming an emulated driver will work. [Microsoft Arm FAQ](https://learn.microsoft.com/en-us/windows/arm/faq), [emulation documentation](https://learn.microsoft.com/en-us/windows/arm/apps-on-arm-x86-emulation).

## Previously held applications

| Application | Evidence found | 1.2.3 behavior and limits |
|---|---|---|
| TeamViewer | Official Windows ARM64 download exists. WinGet 15.82.6 currently lists x86/x64. The downloaded vendor installer has a valid TeamViewer Germany signature and contains an ARM64 TeamViewer.exe, version 15.80.4. This official ARM EXE is older than the current x64 manifest. | Try official native vendor setup first, then WinGet ARM64/x64/x86 if it is unavailable. Existing installs are preserved. [Vendor download portal](https://www.teamviewer.com/en/download/portal/windows/). |
| Wireshark | Official 4.6.9 ARM64 download exists; its current WinGet manifest only lists x64. Valid Wireshark Foundation signature; extracted Wireshark.exe is ARM64. | Try native vendor installer first, then emulation. Silent installation does not install Npcap. Saved captures can be analyzed without it; live capture requires a separate native-capable Npcap installation. [Downloads](https://www.wireshark.org/download.html), [installer documentation](https://www.wireshark.org/docs/wsug_html_chunked/ChBuildInstallWinInstall.html), [Npcap](https://npcap.com/guide/npcap-users-guide.html). |
| Parsec | Current WinGet 150.104.1.0 provides x64. No official Windows ARM64 package located. Current vendor instructions describe bundled virtual USB/display drivers and explicit exclusion switches. | Try native if subsequently published, otherwise Intel emulation. Both WinGet and vendor fallback use `/nodrivers` on ARM; hosting, GPU acceleration and controller forwarding are not established. [Vendor installation and driver switches](https://support.parsec.app/hc/en-us/articles/32381199341716-Parsec-App-for-Windows), [hardware requirements](https://support.parsec.app/hc/en-us/articles/32381568346644-Hardware-and-Software-Compatibility). |
| Logi Options+ | Current WinGet 2.8.981479 lists an x64 online installer. No separate official native Windows ARM64 download located. | Permit emulation; the web installer may download additional device integrations. Successful installation does not validate all device functions. [Vendor product and download page](https://www.logitech.com/en-us/software/logi-options-plus), [deployment instructions](https://hub.sync.logitech.com/options/post/online-options-mass-installation-windows-u6JpoV9rgMDWr5i). |
| BlueBubbles | Vendor documentation directs Windows users to Store product 9P3XF8KJ0LSM. Official desktop releases contain an MSIX and Windows EXE, without a separately named Windows ARM64 asset. | Let Store choose an applicable package, including emulation. Presence detection also checks official MSIX identity `23344BlueBubbles.BlueBubbles`, verified in the vendor pubspec configuration. Store availability, client operation and the required Mac server remain separate checks. [Desktop installation](https://docs.bluebubbles.app/client/desktop-app-installation), [official releases](https://github.com/BlueBubblesApp/bluebubbles-app/releases). |
| Balena Etcher | WinGet 2.1.7 lists x64. Official Windows release assets are x64; ARM64 assets are for macOS. | Permit x64 emulation; actual elevated USB/SD writing still needs testing. No unofficial ARM fork is substituted. [Official releases](https://github.com/balena-io/etcher/releases). |
| Raspberry Pi Imager | WinGet 2.0.8 lists x64. Official 2.0.12 release has one Windows EXE; its separately named ARM64 assets are Linux packages. | Permit Intel emulation. The runtime WinGet query selects any future native manifest first. Actual imaging needs device testing. [Official releases](https://github.com/raspberrypi/rpi-imager/releases), [vendor download page](https://www.raspberrypi.com/software/). |
| Unraid USB Creator | Current WinGet 1.1.0 lists an x86 installer with no declared scope. Vendor Windows link points to the same USB Creator download family. | Try ARM64/x64, then the available x86 package. Omit an invented machine-scope filter. Creating media is a later manual operation; PC Setup only installs the utility. [Vendor downloads](https://unraid.net/download). |
| Rainmeter | WinGet 4.5.28.3936 lists an x86 installer wrapper. Vendor documents that the wrapper selects 64-bit application files on a new 64-bit Windows installation. | Try native/x64 if available, then x86 wrapper. Plug-ins and hardware monitoring need separate testing. [Installation documentation](https://docs.rainmeter.net/manual/installing-rainmeter/), [official releases](https://github.com/rainmeter/rainmeter/releases). |

The formerly skipped applications are selectable too: NVCleanstall (x86 package), Afterburner (x86 package), Power Automate Desktop (x64), UltraISO (x86) and K-Lite Standard (x86/x64). These are installation attempts, not claims of full ARM compatibility. NVCleanstall targets NVIDIA hardware; Intel-only GPU/virtual-drive drivers cannot operate Snapdragon hardware. Microsoft explicitly excludes ARM from Power Automate Desktop support. [NVCleanstall](https://www.techpowerup.com/download/techpowerup-nvcleanstall/), [Power Automate requirements](https://learn.microsoft.com/en-us/power-automate/desktop-flows/requirements), [UltraISO](https://www.ezbsystems.com/enindex.html), [Codec Guide](https://codecguide.com/index.html).

Reviewed manifests are included in the release's Research folder, alongside official release asset metadata. Live WinGet queries, not this snapshot, determine install availability. Hash and publisher checks remain enforced; no hash override or Windows driver enforcement bypass was added.

## Download verification

- TeamViewer official ARM64 download: 77,357,424 bytes; SHA256 `4DA828ADC09E14531121C2F302BEC6A6761A318B6D2B21B9F059B0B4B79BBC70`; publisher TeamViewer Germany GmbH. The dynamic vendor URL is accepted only with a valid expected-publisher signature.
- Wireshark 4.6.9 ARM64: 82,225,432 bytes; SHA256 `E25B68C0A7D8CCC03A325235D1734D71998AF30A0136DC352F3EA5CE0EEFAFC4`; publisher Wireshark Foundation. This fixed release is pinned to that hash and still requires a valid signature.
- Both installer wrappers are x86 (`0x014C`), while the extracted main applications are ARM64 (`0xAA64`). Rejecting the wrappers solely because they are x86 would incorrectly block native installation.

The installers were downloaded/extracted for inspection; neither was executed during development.

## 1.2.2 log analysis

The completed run `f60ddfd341f04f09835edaea8f17e7f7` reported one final application failure: Tor. WinGet installed its portable browser successfully, but `winget list` could not find an uninstall registration. The actual executable exists under the user's Desktop\Tor Browser\Browser folder. Version 1.2.3 recognizes that location and redirected Desktop folders, skips an existing browser, and requires its file to confirm a new installation.

The other selected applications, including Equalizer APO, Steam, Modrinth, Advanced IP Scanner, Transmission and WizTree, passed presence checks. Spotify's Store detection worked. This establishes installation detection, not exhaustive feature testing.

The final report relabelled existing skipped applications as succeeded after presence verification. That reporting bug is fixed: they remain skipped. Actual chosen installer architectures are now persisted and printed instead of relying on the policy label.

The BitLocker WMI warning was followed by a successful manage-bde fallback confirming full decryption. The restore-point frequency warning indicates a recent restore point prevented creating another. Hyper-V, Sandbox and NFS were correctly unavailable on the installed Windows Home edition; .NET Framework 3.5 was already enabled. No change to encryption requirements, restore-point frequency or edition restrictions was made.
