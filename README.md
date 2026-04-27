# OrionBE Launcher

OrionBE Launcher is a Linux Minecraft Bedrock launcher focused on instance management, mod workflows, and smooth GDK runtime setup for Proton.

It acts as an integrated instance + mods manager with LeviLamina-oriented modding workflows, while automating the Linux runtime path (GDK package + Proton + UMU).

## What It Does

- Manages multiple Minecraft Bedrock instances
- Configures Linux runtime dependencies for Bedrock GDK packages
- Integrates mod-enabled launcher flows with a clean desktop UI
- Targets Linux packaging and distribution (including AppImage)

## Feature Status

- ✅ Instance manager
- ✅ Automated Proton + Linux runtime dependency setup
- ✅ Mod integration
- 🚧 Mod search/import
- 🚧 Profile system (player name + skin)
- 🚧 Proxy pass for Microsoft login + online server access
- 🚧 Texture manager
- 🚧 Add-on manager
- 🚧 World manager
- 🚧 RTX integration
- 🚧 BrowseRTX
- 🚧 Friend list + join world integration

## Tech Stack

- .NET `net10.0`
- Avalonia UI
- MVVM Toolkit
- Microsoft.Extensions (DI/Logging/HttpClient)

## Build Requirements

### Required

- Linux (x64 or arm64 target)
- .NET SDK compatible with `net10.0`

### Optional (for AppImage packaging)

- `appimagetool`

### Dependency install script (Linux)

The repo includes [`scripts/install-build-deps.sh`](scripts/install-build-deps.sh), which installs the **.NET 10 SDK** (via your distro when supported, otherwise [Microsoft’s install script](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script)) and can optionally install **`appimagetool`** for AppImage builds (see script header for flags and environment variables).

```bash
chmod +x scripts/install-build-deps.sh
./scripts/install-build-deps.sh
```

Useful options: `--with-appimage`, `--verify` (runs `dotnet restore` + `dotnet build` after setup).

## Build Instructions

### 1) Restore + Build

```bash
dotnet restore
dotnet build OrionBE.sln -c Release
```

### 2) Run (development)

```bash
dotnet run --project OrionBE.Launcher/OrionBE.Launcher.csproj
```

### 3) Publish (self-contained Linux)

```bash
dotnet publish OrionBE.Launcher/OrionBE.Launcher.csproj -c Release -r linux-x64 --self-contained true
```

### 4) Build AppImage (scripted)

```bash
./scripts/build-appimage.sh linux-x64 Release
```

Output artifacts are generated under:

- `artifacts/publish/`
- `artifacts/AppDir/`
- `artifacts/appimage/`

## Notes on Linux Runtime

The launcher prepares Minecraft Bedrock GDK execution on Linux using Proton-compatible runtime tooling and cacheable install paths.  
Runtime/tool fetching and package handling are built into the launcher installation flow.

## Thanks & References

- LiteLDev: [https://github.com/LiteLDev](https://github.com/LiteLDev)
- GDK Proton used by this project: [https://github.com/raonygamer/gdk-proton](https://github.com/raonygamer/gdk-proton)
- UMU launcher used by this project: [https://github.com/raonygamer/umu-launcher](https://github.com/raonygamer/umu-launcher)
- Amethyst project: [https://github.com/AmethystAPI/] and [https://github.com/FrederoxDev/Amethyst-Launcher]
