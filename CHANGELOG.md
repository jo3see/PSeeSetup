# Changelog

## 1.2.6 — first public prerelease

- Added Uninstall Apps with 24 entries, including separate desktop and Store Copilot targets.
- Defaulted every removal to unchecked; disabled absent, protected and unsupported entries.
- Limited Store removals to the signed-in user. Added MSI, registered EXE, Deno and Recall removal paths with post-removal checks, logging and saved-state restart handling.
- Kept Sticky Notes (new) disabled because it is part of OneNote.
- Removed publisher/signature restrictions from selected app removal after live testing. Installation download verification remains enforced.
- Kept removals isolated from failed-install retries.
- Automated validation: 90 checks passed on native ARM64 and under x64 emulation. Physical x64 hardware testing remains outstanding.

### Internal development before the first public release

- **1.2.5:** versioned, signed Unraid download; Pi Imager conflict guard; direct verified TeamViewer ARM MSI and improved file detection.
- **1.2.4:** PSeeSetup branding; Applications → Tweaks → Execute interface; removed presets, Review and simulation; added Open log.
- **1.2.3:** native ARM64 preference with approved x64/x86 fallback, vendor research and retry JSON selection fixes.
- **1.2.2 / 1.2.1:** installation/reporting fixes and encryption-dialog layout improvements.
- **1.2 / 1.1.8:** architecture-aware development based on the earlier internal PC Setup project.

Old builds are not published as downloadable releases because they contain superseded behavior and known bugs. Source history starts with this reviewed snapshot; no historical commits are fabricated.
