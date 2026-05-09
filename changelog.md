# OrionBE Launcher - Changelog

## 0.3.2

### Added
- Added Bedrock online bootstrap during instance installation:
  - downloads and places `ca-bundle.crt` in `etc/ssl/certs`
  - downloads a compatible libcurl package and deploys it as `Content/Xcurl.dll`
- Added deterministic Bedrock executable discovery, prioritizing `Content/Minecraft.Windows.exe`.
- Added automatic copy of `SystemFiles/system32/combase.dll` into the game executable directory at instance creation time.
- Added automatic `options.txt` patching on launch to enforce:
  - `do_not_show_multiplayer_online_safety_warning:1`
- Added first-run dependency verification:
  - runs once on first launcher startup
  - checks required runtime commands/assets used by install/launch flows
  - records a marker file after execution
- Added temporary per-instance Linux compatibility options in instance creation:
  - `Enable GNOME Compatibility profile (temporary)`
  - `Use X11 fallback on launch (temporary)`
  - `Collect launch diagnostics log for issue reports`

### Changed
- Improved Linux launch safety:
  - blocks duplicate launch attempts for the same instance while launch is in progress
  - prevents launching an instance that is already running
- Added launch-state UI feedback:
  - Play button now shows `Launching...`
  - launch controls are disabled while launch is being started
- Improved Linux runtime setup for newer Bedrock builds by attempting `GameInputRedist.msi` installation in the Wine prefix.
- Added startup dependency warning UI:
  - shows a dependency report dialog when missing items are detected on first run
  - keeps startup non-blocking even if the check fails unexpectedly
- Improved launch diagnostics for issue triage:
  - when enabled per instance, launch stdout/stderr are stored under `instances/<instance>/logs/`
  - intended as temporary evidence collection for GNOME/Zorin minimize/workspace crash reports

### Temporary Notice
- The GNOME compatibility profile and X11 fallback are temporary test workarounds.
- If community testing confirms they reduce crash/hang reports, they will be promoted to native defaults in the next version.

### Technical
- Added `Tmds.DBus.Protocol` explicit dependency override to a fixed secure version.
- Added `SystemFiles/**` to launcher output copy rules for runtime availability.
- Added `IStartupDependencyCheckService` with first-launch marker persistence under `~/OrionBE`.
