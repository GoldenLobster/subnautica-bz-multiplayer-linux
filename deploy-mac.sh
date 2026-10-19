#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=================================================="
echo "Subnautica Below Zero Multiplayer — macOS Deploy"
echo "=================================================="

# Check that patched files exist
if [ ! -f "$SCRIPT_DIR/Subnautica.Multiplayer.exe" ]; then
    echo "Error: Subnautica.Multiplayer.exe not found in $SCRIPT_DIR."
    echo "Please run './patch-mac.sh' first!"
    exit 1
fi

if [ ! -f "$SCRIPT_DIR/Subnautica.API.Patched.dll" ]; then
    echo "Error: Subnautica.API.Patched.dll not found in $SCRIPT_DIR."
    echo "Please run './patch-mac.sh' first!"
    exit 1
fi

# 1. Search for game directory in Whisky, CrossOver, or Wine
FOUND_GAME_DIRS=()

if [ -n "$SUBNAUTICA_GAME_DIR" ] && [ -d "$SUBNAUTICA_GAME_DIR" ]; then
    FOUND_GAME_DIRS+=("$SUBNAUTICA_GAME_DIR")
fi

# Search Whisky bottles
WHISKY_BOTTLES_DIR="$HOME/Library/Containers/com.isaacmarovitz.Whisky/Bottles"
if [ -d "$WHISKY_BOTTLES_DIR" ]; then
    while IFS= read -r dir; do
        [ -d "$dir" ] && FOUND_GAME_DIRS+=("$dir")
    done < <(find "$WHISKY_BOTTLES_DIR" -maxdepth 7 -type d -name "SubnauticaZero" 2>/dev/null || true)
fi

# Search CrossOver bottles
CROSSOVER_BOTTLES_DIR="$HOME/Library/Application Support/CrossOver/Bottles"
if [ -d "$CROSSOVER_BOTTLES_DIR" ]; then
    while IFS= read -r dir; do
        [ -d "$dir" ] && FOUND_GAME_DIRS+=("$dir")
    done < <(find "$CROSSOVER_BOTTLES_DIR" -maxdepth 7 -type d -name "SubnauticaZero" 2>/dev/null || true)
fi

# Search plain Wine prefixes (WINEPREFIX or default ~/.wine)
WINE_DIRS=()
if [ -n "$WINEPREFIX" ] && [ -d "$WINEPREFIX" ]; then
    WINE_DIRS+=("$WINEPREFIX")
fi
if [ -d "$HOME/.wine" ]; then
    WINE_DIRS+=("$HOME/.wine")
fi

for wdir in "${WINE_DIRS[@]}"; do
    STANDARD_WINE_PATHS=(
        "$wdir/drive_c/Program Files (x86)/Steam/steamapps/common/SubnauticaZero"
        "$wdir/drive_c/Program Files/Steam/steamapps/common/SubnauticaZero"
    )
    for p in "${STANDARD_WINE_PATHS[@]}"; do
        if [ -d "$p" ]; then
            FOUND_GAME_DIRS+=("$p")
        fi
    done
    # Deep search in wine prefix if not in standard path
    if [ ${#FOUND_GAME_DIRS[@]} -eq 0 ]; then
        while IFS= read -r dir; do
            [ -d "$dir" ] && FOUND_GAME_DIRS+=("$dir")
        done < <(find "$wdir" -maxdepth 7 -type d -name "SubnauticaZero" 2>/dev/null || true)
    fi
done

if [ ${#FOUND_GAME_DIRS[@]} -eq 0 ]; then
    echo ""
    echo "Warning: Could not automatically locate the Windows 'SubnauticaZero' game directory."
    echo ""
    echo "If you have installed the game in a custom bottle path, specify it directly:"
    echo "  SUBNAUTICA_GAME_DIR=\"/path/to/drive_c/.../SubnauticaZero\" ./deploy-mac.sh"
    echo ""
    exit 1
fi

for GAME_DIR in "${FOUND_GAME_DIRS[@]}"; do
    echo ""
    echo "Found game installation:"
    echo "  -> $GAME_DIR"
    echo "Deploying Subnautica.Multiplayer.exe..."
    cp -fv "$SCRIPT_DIR/Subnautica.Multiplayer.exe" "$GAME_DIR/Subnautica.Multiplayer.exe"

    # Find the drive_c root for this installation
    BOTTLE_DRIVE_C=$(echo "$GAME_DIR" | sed -E 's|(/drive_c)(/.*)?$|\1|')
    if [ -d "$BOTTLE_DRIVE_C/users" ]; then
        for USER_DIR in "$BOTTLE_DRIVE_C/users"/*; do
            if [ -d "$USER_DIR" ]; then
                DEP_DIR="$USER_DIR/AppData/Roaming/.botbenson/Subnautica Below Zero/Game/Dependencies"
                mkdir -p "$DEP_DIR"
                echo "Deploying Subnautica.API.dll to bottle AppData:"
                echo "  -> $DEP_DIR/Subnautica.API.dll"
                cp -fv "$SCRIPT_DIR/Subnautica.API.Patched.dll" "$DEP_DIR/Subnautica.API.dll"
            fi
        done
    fi
done

echo ""
echo "=================================================="
echo "Deployment Complete on macOS!"
echo "=================================================="
echo "How to launch the mod in Whisky / CrossOver:"
echo "1. Open Whisky (or CrossOver)."
echo "2. Open your Steam bottle."
echo "3. Click 'Run...' (or pin a shortcut) and select:"
echo "   Subnautica.Multiplayer.exe (inside your SubnauticaZero game folder)"
echo "4. In the launcher window, click 'Play' to start the mod!"
echo ""
echo "See MAC_GUIDE.md for multiplayer hosting/joining details on Mac."
