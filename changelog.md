# OrionBE Launcher - Changelog

## 0.3.3

### Added
- **Add instance:** toggle to keep the add-instance screen open after a **successful** installation so you can read the full log before going back (default remains “return home when installation succeeds”).
- **Instance settings → Bedrock update:** checks the catalog for a **strictly newer** Bedrock build in the **same channel** (`release` vs `preview`). Button **Update to latest in this channel** is only enabled when an upgrade exists; the section uses reduced opacity when no update is available. Stable builds never jump to preview (and vice versa); older versions are never offered.
- **`IBedrockVersionCatalogService.TryGetLatestUpgradeInSameChannelAsync`** and **`IInstallationService.UpgradeInstanceToLatestEligibleAsync`** to refresh game files from the `.msixvc` pipeline (mods re-copied into `game/mods` when mods are enabled).

### Changed
- Linux (Proton/umu): set `SteamAppId` / `SteamGameId` / `STEAM_COMPAT_APP_ID` to a conventional non-Steam placeholder (`480`, Spacewar) and, when `/usr/bin/env` exists, invoke **`env SteamAppId=… SteamGameId=… STEAM_COMPAT_APP_ID=… umu-run …`** so wrappers that drop inherited env still pass a numeric Steam app id where supported.
- **Removed** experimental per-instance Linux compatibility options from **Add instance** (GNOME compatibility profile, X11 fallback, launch diagnostics). Remaining focus/minimize, workspace, or compositor issues on some desktops are **likely limitations or bugs in Proton/Wine** rather than something the launcher can fully paper over; we may revisit mitigations in future releases as upstream improves.
- If `umu-run` exits with a non-zero exit code, the launcher shows an error dialog instead of failing silently.
- User-facing installation logs, online-bootstrap messages, instance settings UI, and related developer comments are now consistently in English.
- Instance cards show **Running…** and disable Play while that instance’s Bedrock process is detected (polls until exit); Settings stays available. Windows detects `Minecraft.Windows.exe` under the instance game folder; Linux keeps `/proc` scanning as before.

### Fixed
- **Play** no longer triggers Avalonia **“Call from invalid thread”** after launch: UI state updates (`IsLaunching` / `IsGameRunning`) are marshalled back to the UI thread after `ConfigureAwait(false)` on the game launch await.

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

### Technical
- Added `Tmds.DBus.Protocol` explicit dependency override to a fixed secure version.
- Added `SystemFiles/**` to launcher output copy rules for runtime availability.
- Added `IStartupDependencyCheckService` with first-launch marker persistence under `~/OrionBE`.
