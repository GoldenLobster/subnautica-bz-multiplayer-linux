#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=================================================="
echo "Subnautica Below Zero Multiplayer — macOS Patcher"
echo "=================================================="

DOWNLOAD_URL="https://subnauticamultiplayer.com/Subnautica.Multiplayer.exe"

# Allow force re-download with --download or -d
if [ "$1" = "--download" ] || [ "$1" = "-d" ] || [ "$1" = "--fresh" ]; then
    echo "Downloading fresh official launcher from $DOWNLOAD_URL..."
    rm -f "Subnautica.Multiplayer.exe" "Subnautica.Multiplayer.exe.original"
fi

if [ ! -f "Subnautica.Multiplayer.exe" ] && [ ! -f "Subnautica.Multiplayer.exe.original" ]; then
    echo "Subnautica.Multiplayer.exe not found."
    echo "Automatically downloading official launcher from:"
    echo "  $DOWNLOAD_URL"
    if command -v curl >/dev/null 2>&1; then
        curl -fL --progress-bar -o "Subnautica.Multiplayer.exe" "$DOWNLOAD_URL"
    elif command -v wget >/dev/null 2>&1; then
        wget -q --show-progress -O "Subnautica.Multiplayer.exe" "$DOWNLOAD_URL"
    else
        echo "Error: Neither curl nor wget found. Please download $DOWNLOAD_URL manually and place it in $SCRIPT_DIR."
        exit 1
    fi
    echo "Download complete!"
fi

# Check for dotnet SDK
if command -v dotnet >/dev/null 2>&1; then
    echo "Found .NET SDK ('dotnet'). Running Mono.Cecil patcher..."
    dotnet run --project "$SCRIPT_DIR/tools/LinuxPatcher/LinuxPatcher.csproj"
elif [ -f "$SCRIPT_DIR/Subnautica.API.Patched.dll" ] && [ -f "$SCRIPT_DIR/Subnautica.Multiplayer.exe" ]; then
    echo "Note: .NET SDK ('dotnet') not found, but pre-patched mod assemblies"
    echo "(Subnautica.Multiplayer.exe and Subnautica.API.Patched.dll) are already available in this folder."
    echo "Ready to deploy directly!"
else
    echo "Error: .NET SDK ('dotnet') is not installed, and pre-patched files are missing."
    echo "Install .NET via Homebrew ('brew install dotnet-sdk') or download it from https://dotnet.microsoft.com/download"
    exit 1
fi

echo ""
echo "Patch complete! Run './deploy-mac.sh' to deploy to your Whisky/CrossOver bottle."
echo "See MAC_GUIDE.md for step-by-step instructions."
