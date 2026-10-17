#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Subnautica Below Zero Multiplayer Linux Patcher ==="

if ! command -v dotnet >/dev/null 2>&1; then
    echo "Error: .NET SDK ('dotnet') is not installed or not in PATH."
    echo "Please install the .NET 8 SDK or runtime to run this patcher."
    exit 1
fi

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

echo "Running Mono.Cecil patcher..."
dotnet run --project "$SCRIPT_DIR/tools/LinuxPatcher/LinuxPatcher.csproj"

echo ""
echo "Patch complete! See LINUX_GUIDE.md for setup and play instructions."
