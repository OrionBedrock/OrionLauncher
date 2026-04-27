#!/usr/bin/env bash
# Installs dependencies required to build OrionLauncher and optional tooling used by this repo.
#
# Usage:
#   ./scripts/install-build-deps.sh
#   ./scripts/install-build-deps.sh --with-appimage
#   ./scripts/install-build-deps.sh --verify
#   DOTNET_CHANNEL=10.0 ./scripts/install-build-deps.sh
#
# Environment variables:
#   DOTNET_INSTALL_DIR   SDK install path (default: $HOME/.dotnet) when using Microsoft's installer
#   DOTNET_CHANNEL       Microsoft install script channel (default: 10.0)
#   SKIP_DOTNET          set to 1 to skip .NET install/verification (extras only)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WITH_APPIMAGE=0
DO_VERIFY=0

for arg in "$@"; do
  case "$arg" in
    --with-appimage) WITH_APPIMAGE=1 ;;
    --verify) DO_VERIFY=1 ;;
    -h|--help)
      sed -n '1,14p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg (try --help)" >&2
      exit 1
      ;;
  esac
done

DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"

has_sdk_10() {
  command -v dotnet >/dev/null 2>&1 || return 1
  dotnet --list-sdks 2>/dev/null | grep -qE '^10\.'
}

echo "== OrionLauncher — build dependencies =="
echo "Project root: $ROOT"

if [[ "${SKIP_DOTNET:-0}" != "1" ]]; then
  if has_sdk_10; then
    echo "[.NET] SDK 10.x already available: $(command -v dotnet)"
    dotnet --list-sdks
  else
    echo "[.NET] SDK 10.x not found; attempting installation…"
    INSTALLED=0

    if [[ -f /etc/os-release ]]; then
      # shellcheck source=/dev/null
      . /etc/os-release
    else
      ID=""
    fi

    case "${ID:-}" in
      arch|cachyos|manjaro|endeavouros)
        if command -v pacman >/dev/null 2>&1; then
          echo "[.NET] Pacman: installing dotnet-sdk…"
          sudo pacman -S --needed --noconfirm dotnet-sdk && INSTALLED=1 || true
        fi
        ;;
      fedora)
        if command -v dnf >/dev/null 2>&1; then
          echo "[.NET] DNF: installing dotnet-sdk…"
          sudo dnf install -y dotnet-sdk && INSTALLED=1 || true
        fi
        ;;
      ubuntu|pop|linuxmint|zorin)
        if command -v apt-get >/dev/null 2>&1 && command -v wget >/dev/null 2>&1; then
          echo "[.NET] APT (Ubuntu): Microsoft repo + dotnet-sdk-10.0…"
          ver="${VERSION_ID:-22.04}"
          set +e
          wget -q "https://packages.microsoft.com/config/ubuntu/${ver}/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb
          set -e
          if [[ -f /tmp/packages-microsoft-prod.deb ]]; then
            sudo dpkg -i /tmp/packages-microsoft-prod.deb || sudo apt-get -f install -y
            sudo apt-get update
            sudo apt-get install -y dotnet-sdk-10.0 && INSTALLED=1 || true
          fi
        fi
        ;;
      debian)
        echo "[.NET] Debian: use Microsoft's installer below or packages from https://learn.microsoft.com/dotnet/core/install/linux-debian"
        ;;
      opensuse*|suse)
        if command -v zypper >/dev/null 2>&1; then
          echo "[.NET] Zypper: installing dotnet-sdk-10.0…"
          sudo zypper install -y dotnet-sdk-10.0 && INSTALLED=1 || true
        fi
        ;;
    esac

    if [[ "$INSTALLED" -eq 0 ]] || ! has_sdk_10; then
      echo "[.NET] Official Microsoft installer → $DOTNET_INSTALL_DIR (channel $DOTNET_CHANNEL)…"
      tmp_script="$(mktemp)"
      curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$tmp_script"
      chmod +x "$tmp_script"
      "$tmp_script" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR"
      rm -f "$tmp_script"
      export PATH="$DOTNET_INSTALL_DIR:$PATH"
      export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
    fi

    if ! has_sdk_10; then
      echo "" >&2
      echo "Add the SDK to PATH (new shell or ~/.profile):" >&2
      echo "  export DOTNET_ROOT=\"$DOTNET_INSTALL_DIR\"" >&2
      echo "  export PATH=\"\$DOTNET_ROOT:\$PATH\"" >&2
      echo "" >&2
      echo "Error: SDK 10.x still not found after installation." >&2
      exit 1
    fi

    echo "[.NET] OK — installed SDKs:"
    dotnet --list-sdks
  fi
else
  echo "[.NET] SKIP_DOTNET=1 — skipping SDK check/install."
fi

if [[ "$WITH_APPIMAGE" -eq 1 ]]; then
  echo ""
  echo "== Optional: appimagetool (./scripts/build-appimage.sh) =="
  if command -v appimagetool >/dev/null 2>&1; then
    echo "[AppImage] appimagetool already available: $(command -v appimagetool)"
  else
    INST_APPIMAGE=0
    if [[ -f /etc/os-release ]]; then
      # shellcheck source=/dev/null
      . /etc/os-release
    fi
    case "${ID:-}" in
      arch|cachyos|manjaro)
        if command -v pacman >/dev/null 2>&1; then
          echo "[AppImage] Trying pacman (package may be in the AUR)…"
          sudo pacman -S --needed --noconfirm appimagetool 2>/dev/null && INST_APPIMAGE=1 || true
        fi
        ;;
      ubuntu|debian)
        sudo apt-get install -y appimagetool 2>/dev/null && INST_APPIMAGE=1 || true
        ;;
    esac

    if [[ "$INST_APPIMAGE" -eq 0 ]] && ! command -v appimagetool >/dev/null 2>&1; then
      bin_dir="${HOME}/.local/bin"
      mkdir -p "$bin_dir"
      gh_url="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
      out="$bin_dir/appimagetool"
      echo "[AppImage] Downloading continuous build to $out …"
      curl -fL "$gh_url" -o "$out"
      chmod +x "$out"
      echo "[AppImage] Installed at $out — ensure ~/.local/bin is on PATH."
    fi

    command -v appimagetool >/dev/null 2>&1 || {
      echo "Warning: appimagetool is still not on PATH. Adjust PATH or set APPIMAGE_TOOL." >&2
    }
  fi
fi

export PATH="$DOTNET_INSTALL_DIR:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$DOTNET_INSTALL_DIR}"

if [[ "$DO_VERIFY" -eq 1 ]]; then
  echo ""
  echo "== Verification (restore + build) =="
  (cd "$ROOT" && dotnet restore OrionBE.sln && dotnet build OrionBE.sln -c Release --no-restore)
fi

echo ""
echo "Done. To build:"
echo "  cd \"$ROOT\" && dotnet restore && dotnet build OrionBE.sln -c Release"
echo "Development run:"
echo "  dotnet run --project \"$ROOT/OrionBE.Launcher/OrionBE.Launcher.csproj\""
