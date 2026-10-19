#!/usr/bin/env bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOGS_DIR="$SCRIPT_DIR/collected_logs"
mkdir -p "$LOGS_DIR"

echo "=== Gathering Subnautica: Below Zero & Mod Logs (macOS) ==="

BOTTLE_ROOTS=()
if [ -n "$WINEPREFIX" ] && [ -d "$WINEPREFIX" ]; then
    BOTTLE_ROOTS+=("$WINEPREFIX")
fi
BOTTLE_ROOTS+=(
    "$HOME/Library/Containers/com.isaacmarovitz.Whisky/Bottles"
    "$HOME/Library/Application Support/CrossOver/Bottles"
    "$HOME/.wine"
)

FOUND_PLAYER_LOG=false
for root in "${BOTTLE_ROOTS[@]}"; do
    if [ -d "$root" ]; then
        while IFS= read -r plog; do
            if [ -f "$plog" ]; then
                echo "Found Unity Player.log:"
                echo "  $plog"
                cp -fv "$plog" "$LOGS_DIR/Player.log"
                FOUND_PLAYER_LOG=true
                break 2
            fi
        done < <(find "$root" -type f -name "Player.log" 2>/dev/null || true)
    fi
done

if [ "$FOUND_PLAYER_LOG" = false ]; then
    echo "Could not automatically locate Player.log in Whisky/CrossOver bottles."
fi

# Find mod logs
FOUND_MOD_LOG=false
for root in "${BOTTLE_ROOTS[@]}"; do
    if [ -d "$root" ]; then
        while IFS= read -r logdir; do
            if [ -d "$logdir" ]; then
                echo "Found mod logs directory:"
                echo "  $logdir"
                cp -fv "$logdir"/*.log "$LOGS_DIR/" 2>/dev/null || true
                FOUND_MOD_LOG=true
            fi
        done < <(find "$root" -type d -path "*/.botbenson/Subnautica Below Zero/Game/Logs" 2>/dev/null || true)
    fi
done

echo ""
echo "=== Summary of Collected Logs in $LOGS_DIR ==="
ls -lh "$LOGS_DIR"

if [ -f "$LOGS_DIR/Player.log" ]; then
    echo ""
    echo "--- Last 40 lines of Player.log ---"
    tail -n 40 "$LOGS_DIR/Player.log"
fi
