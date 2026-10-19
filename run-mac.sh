#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=================================================="
echo "Subnautica Below Zero Multiplayer — Mac Launcher"
echo "=================================================="

# 1. Locate wine binary
WINE_BIN=""
for candidate in wine wine64 /opt/homebrew/bin/wine /opt/homebrew/bin/wine64 /usr/local/bin/wine /usr/local/bin/wine64; do
    if command -v "$candidate" >/dev/null 2>&1; then
        WINE_BIN="$(command -v "$candidate")"
        break
    fi
done

if [ -z "$WINE_BIN" ]; then
    echo "Error: 'wine' command not found."
    echo "Install Wine via Homebrew on Mac:"
    echo "  brew install --cask wine-stable"
    echo "  (or: brew install --cask gcenx-wine-crossover)"
    exit 1
fi

echo "Using Wine: $WINE_BIN"

# 2. Locate Subnautica.Multiplayer.exe
EXE_PATH=""

if [ -n "$SUBNAUTICA_GAME_DIR" ] && [ -f "$SUBNAUTICA_GAME_DIR/Subnautica.Multiplayer.exe" ]; then
    EXE_PATH="$SUBNAUTICA_GAME_DIR/Subnautica.Multiplayer.exe"
fi

SEARCH_DIRS=()
if [ -n "$WINEPREFIX" ] && [ -d "$WINEPREFIX" ]; then
    SEARCH_DIRS+=("$WINEPREFIX")
fi
SEARCH_DIRS+=(
    "$HOME/.wine"
    "$HOME/Library/Containers/com.isaacmarovitz.Whisky/Bottles"
    "$HOME/Library/Application Support/CrossOver/Bottles"
)

if [ -z "$EXE_PATH" ]; then
    for sdir in "${SEARCH_DIRS[@]}"; do
        if [ -d "$sdir" ]; then
            FOUND=$(find "$sdir" -maxdepth 8 -type f -name "Subnautica.Multiplayer.exe" 2>/dev/null | head -n 1)
            if [ -n "$FOUND" ]; then
                EXE_PATH="$FOUND"
                break
            fi
        fi
    done
fi

if [ -z "$EXE_PATH" ] || [ ! -f "$EXE_PATH" ]; then
    echo "Error: Could not locate Subnautica.Multiplayer.exe in your Wine prefix."
    echo "Make sure you ran './deploy-mac.sh' first!"
    echo "You can also specify directly: SUBNAUTICA_GAME_DIR=/path/to/game ./run-mac.sh"
    exit 1
fi

echo "Found launcher: $EXE_PATH"

# 3. Detect WINEPREFIX from EXE_PATH if not set
if [ -z "$WINEPREFIX" ]; then
    if [[ "$EXE_PATH" =~ (.*drive_c) ]]; then
        DETECTED_PREFIX="$(dirname "${BASH_REMATCH[1]}")"
        export WINEPREFIX="$DETECTED_PREFIX"
        echo "Detected WINEPREFIX: $WINEPREFIX"
    fi
fi

# 4. Launch Subnautica.Multiplayer.exe with wine
GAME_DIR="$(dirname "$EXE_PATH")"
echo "Starting Subnautica Below Zero Multiplayer..."
cd "$GAME_DIR"
exec "$WINE_BIN" "$EXE_PATH" "$@"
