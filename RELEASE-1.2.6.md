# PSeeSetup 1.2.6 — Uninstall Apps

Adds a fourth tab before Execute with all 24 requested entries (the user's two Copilot apps are separate rows). Each entry starts unchecked. Read-only discovery scans the signed-in user's Store packages and Start menu, desktop uninstall registrations in both 32-bit and 64-bit registry views, and Recall's Windows feature state. A Refresh button repeats discovery. It reports detection failures as Unknown rather than incorrectly claiming an app is absent.

Rebuilt on October 14, 2026 at the user's request: publisher matching and EXE uninstaller signature requirements have been removed from the removal workflow. Exact installed app identities determine the targets. An unfamiliar or blank publisher no longer greys out an installed app or prevents its registered uninstaller from running. Installer download verification remains unchanged.

Installed, recognized applications can be selected; absent, protected and unsupported entries are greyed out. Merely opening the tab or checking a box does not uninstall anything. Selected names are included in the existing execution confirmation. Installations and tweaks keep their existing selections, so review all four tabs before executing.

## Scope and methods

- Store packages: exact package identities; remove only for the signed-in user. Other accounts and future-user provisioning are retained.
- ASUS Store apps: exact known package identities or an exact ASUS Start menu app name attached to its package family. No wildcard removal of ASUS components.
- Desktop apps and ASUS service installers: exact installed display names across HKLM and HKCU, both registry views. MSI uses the registered product GUID with `/x`, `/qn`, `/norestart`, and a per-app log. EXEs use registered quiet uninstall commands where available, otherwise the vendor's registered uninstall command (which may show confirmation). The registered publisher is logged without imposing a publisher or signature allowlist. No broad service deletion or driver deletion is performed.
- Deno: `winget uninstall --id DenoLand.Deno --exact`; no arbitrary registry command execution.
- Recall: supported Windows optional-feature removal via DISM with `/Remove /NoRestart`; a required restart follows the existing saved-state workflow. Already-removed payloads are unavailable for selection.
- Copilot desktop and Microsoft Copilot Store are independently detected. Microsoft 365 Copilot/Office and Edge are not targeted.
- Xbox/Game Bar: targets their application packages; retains Gaming Services, Xbox Identity Provider, system UI packages, Microsoft Store and framework dependencies.
- Paint: targets Microsoft.Paint; does not remove Paint.NET or Paint 3D.
- Sticky Notes (new): part of OneNote, with no independent supported uninstall. The row remains disabled and explains the limitation when detected; OneNote is not removed.

No removal attempt occurs for an unchecked entry. The engine resolves fresh identities at execution, verifies absence afterward, records `uninstall:` results separately from installations, and preserves selections across resumable reboots. Uninstall failures do not trigger an automatic second attempt. Failed-install retry imports strip every removal selection.

## Sources

- [Microsoft Remove-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage?view=windowsserver2025-ps)
- [Microsoft Recall management and removal](https://learn.microsoft.com/en-us/windows/client-management/manage-recall)
- [Microsoft new Sticky Notes feature in OneNote](https://techcommunity.microsoft.com/blog/microsoft365insiderblog/introducing-the-new-sticky-notes-app-on-windows/4223819)
- [ASUS ScreenXpert installation/removal support](https://www.asus.com/global/support/faq/1044627/)
- [ASUS GlideX support](https://rog.asus.com/uk/support/faq/1050468/)

## Testing

Automated tests exercise exact targets, duplicate Copilot identities, protected/undetected states, removal with unknown or blank publishers and unsigned registered EXEs, MSI argument boundaries, quoted EXE commands, scope, mock removal outcomes, restart reporting, JSON persistence, retry isolation, default-unchecked checkboxes, greyed-out rows and four-tab navigation. Native ARM64 and x64-emulated results are included in Testing.

Development did not uninstall any app, stop any service, remove any feature, decrypt a drive or restart Windows. The development sandbox denied Store enumeration, so live discovery and the actual vendor uninstallers need testing in the released app outside that sandbox. Screenshots use synthetic installed-app data.

All 1.2.5 fixes are retained, including the Unraid/Pi Imager installer conflict guard and verified native TeamViewer MSI installation. Start a new 1.2.6 run; previous run states cannot resume in this version.
