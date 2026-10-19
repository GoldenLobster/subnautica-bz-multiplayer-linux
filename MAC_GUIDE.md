# Subnautica Below Zero Multiplayer — macOS (Plain Wine) Guide

This guide explains how to install, patch, and play the **Subnautica: Below Zero Multiplayer Mod** on macOS (Apple Silicon M1/M2/M3/M4 and Intel) using **Plain Wine** (via Homebrew and Rosetta 2).

---

## How It Works on Mac

Because the multiplayer mod is built as a Windows desktop application (`Subnautica.Multiplayer.exe`) with .NET engine patches, Mac users run the **Windows version** of Subnautica: Below Zero inside a Wine prefix.

All the Linux/Wine compatibility patches we built (bypassing NetBird, Windows Firewall COM, TLS 1.2/1.3, and offline resilience) run directly in Wine on macOS under Rosetta 2.

---

## Quick Setup (4 Steps)

### Step 1: Install Wine via Homebrew
Open Terminal on your Mac and install Wine:
```bash
# Recommended for gaming with DirectX/Metal support:
brew install --cask gcenx-wine-crossover

# (Or standard upstream Wine):
# brew install --cask wine-stable
```
*(On Apple Silicon Macs, macOS will prompt you to install Rosetta 2 if you haven't already: `softwareupdate --install-rosetta`).*

---

### Step 2: Install Windows Steam & Subnautica: Below Zero
1. Download the official Windows Steam installer (`SteamSetup.exe` from [store.steampowered.com](https://store.steampowered.com/about/)).
2. Run the installer with Wine:
   ```bash
   wine ~/Downloads/SteamSetup.exe
   ```
   *(Wine will prompt to automatically install `wine-mono` and `wine-gecko` if needed — click Install).*
3. Launch Windows Steam:
   ```bash
   wine ~/.wine/drive_c/Program\ Files\ \(x86\)/Steam/steam.exe
   ```
4. Log into Steam and install **Subnautica: Below Zero** (this installs the Windows build into `~/.wine`).

---

### Step 3: Deploy the Mod
Navigate to this repository in Terminal and run:
```bash
./deploy-mac.sh
```
> [!NOTE]
> `deploy-mac.sh` automatically scans your Wine prefix (`~/.wine` or `$WINEPREFIX`), finds `SubnauticaZero`, copies `Subnautica.Multiplayer.exe` into the game folder, and installs `Subnautica.API.dll` into the Wine AppData folder.

---

### Step 4: Launch the Game
To start the multiplayer mod, simply run the launcher script from this repository:
```bash
./run-mac.sh
```
This automatically finds Wine and launches `Subnautica.Multiplayer.exe`. In the mod window, click **Play** to start the game!

---

## Joining & Hosting Multiplayer Games

### Joining a Game (Recommended for Mac)
If your friend on Linux or Windows is hosting:
1. Ensure both machines are connected via **Tailscale** (Tailscale is available in the Mac App Store) or on the same local Wi-Fi.
2. In the game main menu, click **Multiplayer** -> **Join Server**.
3. Click **Add Server**.
4. Enter a name and the host's IP address (e.g. `100.x.y.z` for Tailscale or `192.168.x.y` for LAN).
5. Click **Connect**!
> [!TIP]
> **No port configuration is required on macOS when joining!** The client connects using standard outgoing ports that require no root or administrative permissions.

### Hosting a Game on Mac
The mod's local server listens on UDP port `666`. 

On macOS (Darwin BSD kernel), ports below 1024 are strictly privileged for root (macOS does not have Linux's `net.ipv4.ip_unprivileged_port_start` sysctl). 

**Recommendation**: When playing between Linux and Mac, have the **Linux or Windows player host**, and let the Mac player join.

---

## Checking Logs on macOS

If you run into any connection issues, run:
```bash
./get-logs-mac.sh
```
This automatically locates `Player.log` and the `.botbenson` mod network logs inside your Wine prefix and copies them to `collected_logs/` for easy inspection.
