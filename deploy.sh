#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Deploying Patched Subnautica Below Zero Multiplayer ==="

# 1. Determine Steam / Game paths
GAME_DIR="${SUBNAUTICA_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/SubnauticaZero}"
if [ ! -d "$GAME_DIR" ]; then
    # Try Flatpak path
    if [ -d "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/SubnauticaZero" ]; then
        GAME_DIR="$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/SubnauticaZero"
    fi
fi

if [ ! -d "$GAME_DIR" ]; then
    echo "Warning: Could not automatically locate SubnauticaZero game directory at: $GAME_DIR"
    echo "Please specify with: SUBNAUTICA_GAME_DIR=/path/to/game ./deploy.sh"
else
    echo "Found game directory: $GAME_DIR"
    echo "Copying Subnautica.Multiplayer.exe..."
    cp -fv "$SCRIPT_DIR/Subnautica.Multiplayer.exe" "$GAME_DIR/Subnautica.Multiplayer.exe"
fi

# 2. Deploy Subnautica.API.Patched.dll directly into Proton prefix dependencies
PFX_DIRS=(
    "$HOME/.local/share/Steam/steamapps/compatdata/848450/pfx/drive_c/users/steamuser/AppData/Roaming/.botbenson/Subnautica Below Zero/Game/Dependencies"
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/848450/pfx/drive_c/users/steamuser/AppData/Roaming/.botbenson/Subnautica Below Zero/Game/Dependencies"
)

DEPLOYED_API=false
for pfx_dep in "${PFX_DIRS[@]}"; do
    if [ -d "$(dirname "$pfx_dep")" ]; then
        mkdir -p "$pfx_dep"
        echo "Copying Subnautica.API.Patched.dll to Proton prefix:"
        echo "  $pfx_dep/Subnautica.API.dll"
        cp -fv "$SCRIPT_DIR/Subnautica.API.Patched.dll" "$pfx_dep/Subnautica.API.dll"
        DEPLOYED_API=true
    fi
done

if [ "$DEPLOYED_API" = false ]; then
    echo "Note: Proton prefix dependencies folder not found yet. The launcher will extract it automatically on startup."
fi

echo ""
echo "=== Deployment Complete! ==="
echo "Launch Subnautica: Below Zero from Steam, click 'Multiplayer' -> 'Host Game' -> select your world!"
