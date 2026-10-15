# PSeeSetup 1.2.5

This testing release fixes Unraid downloads and TeamViewer ARM installation/detection. It retains the Applications → Tweaks → Execute interface and Open log action.

## Unraid

The last run failed with WinGet 0x8A150011 because its 1.1.0 manifest points at a replaced, moving download. Use the official, versioned next-generation release instead:

- [Official release](https://github.com/unraid/usb-creator-next/releases/tag/v2.0.11.1-unraid.0)
- File: unraid-usb-creator-v2.0.11.1-unraid.0.exe
- SHA256: `7E29F1F907488E5FEDC6044E1F2256ABC55250B21BAFEDE28E78AF02EBEDC7C4`, matched against the GitHub release asset digest.
- Windows Authenticode: Valid; Lime Technology, Inc.
- Inno Setup switches: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`.
- x86 installer bootstrapper; x64 application runs under emulation on Windows ARM64. USB-writing functionality still requires a real-device test.

The [vendor installer source](https://github.com/unraid/usb-creator-next/blob/v2.0.11.1-unraid.0/src/windows/rpi-imager.iss.in) retains shared Raspberry Pi Imager identifiers and explicitly removes earlier Pi Imager installations. PSeeSetup holds Unraid when Pi Imager is detected. This is a vendor co-installation limitation; selecting both applications can leave Unraid requiring verification. No automatic uninstall is performed by PSeeSetup. The vendor WinUSB INF includes ARM64 entries and uses Windows' built-in WinUSB driver, but its device installation was not tested.

## TeamViewer

The last run tried TeamViewer_Setup_ARM64.exe /S, received exit 0 without confirmed installation, then installed the WinGet x64 MSI. A Windows Installer help popup appeared during the run. Its precise internal origin was not captured; the new path avoids that EXE wrapper entirely.

- [Official ARM64 package](https://download.teamviewer.com/download/version_15x/update/Update_msi_15.82.6_arm64.zip)
- Archive SHA256: `24DA6DF7A481008FF85995E7F58CD25A867E639C0C2C3F8B963FBC63147A1582`.
- Extract only `update.msi`; verify its Windows signature and MSI summary architecture before execution.
- Windows Authenticode: Valid; TeamViewer Germany GmbH.
- Read-only MSI metadata: Arm64;1033, ProductVersion 15.82.6.0.
- [Vendor release announcement](https://community.teamviewer.com/English/discussion/151921/windows-v15-82-6) and [MSI deployment instructions](https://www.teamviewer.com/apac/global/support/knowledge-base/teamviewer-remote/deployment/mass-deployment-user-guide/deploy-teamviewer-host-or-full-client-9-10/).
- Explicit `/i`, `/qn`, `/norestart` and verbose log arguments keep MSI command syntax valid and suppress installer UI. TeamViewer-ARM64-install.log is written in the run directory.
- Actual TeamViewer.exe detection includes Program Files, Program Files (x86), and ProgramW6432. Native installation requires an ARM64 executable; an existing x64 executable cannot confirm native success.
- A short settling wait handles delayed registration. Unconfirmed or failed started ARM installation reports NeedsVerification without installing x64 over it. If download or validation fails before execution, verified WinGet emulation remains available.
- An existing installation is skipped. Uninstall it manually only if you want to test fresh native installation; the app does not silently migrate architectures.

## Validation

The release includes native ARM64 and emulated x64 test results. Tests check argument boundaries, native metadata, publisher/hash validation, rejection of a signed x64 MSI as ARM64, actual-file detection, skip behavior, protection against a second installer, and the Unraid/Pi Imager conflict guard. Existing UI, retry, architecture, encryption-dialog and result-report regressions are also checked.

No live installation, encryption change, tweak, reboot, service start or USB write was performed during development. The next user run is the live integration test.

Extract the complete ZIP and launch the top-level PSeeSetup.exe. Use a fresh run; saved runs from older versions cannot be resumed. Failed application selections can still be imported. GitHub publishing remains deferred.
