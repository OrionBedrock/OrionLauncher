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

### Changed
- Improved Linux launch safety:
  - blocks duplicate launch attempts for the same instance while launch is in progress
  - prevents launching an instance that is already running
- Added launch-state UI feedback:
  - Play button now shows `Launching...`
  - launch controls are disabled while launch is being started
- Improved Linux runtime setup for newer Bedrock builds by attempting `GameInputRedist.msi` installation in the Wine prefix.

### Technical
- Added `Tmds.DBus.Protocol` explicit dependency override to a fixed secure version.
- Added `SystemFiles/**` to launcher output copy rules for runtime availability.
