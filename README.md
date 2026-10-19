> [!NOTE]
>This ONLY works with steam, this patch is not adapted to work with the epicgames launcher.

# Subnautica Below Zero Multiplayer — Linux & Steam (Proton) Guide

This guide explains how to install, patch, and play the **Subnautica: Below Zero Multiplayer Mod** on Linux through Steam using Proton, with direct IP / Tailscale / LAN multiplayer. 

**Although this patch was not developed with macOS in mind, it should work if you use the windows version of steam and the windows version of Subnautica: Below Zero(using wine or proton). See a more in-depth explanation and guide [below](#mac)**

---

## Quick Start (3 Steps)

### Step 1: Enable UDP Port 666
The mod uses UDP port 666 for hosting and joining. Run the helper script to permit unprivileged access to this port:
```bash
./enable-port-666.sh
```
*(Or manually run: `sudo sysctl -w net.ipv4.ip_unprivileged_port_start=666`)*

### Step 2: Download & Patch the Launcher
Run the patch script from this directory:
```bash
./patch.sh
```
> [!NOTE]
> If `Subnautica.Multiplayer.exe` is not already present, `patch.sh` automatically downloads the latest official launcher from `https://subnauticamultiplayer.com/Subnautica.Multiplayer.exe` and applies all Linux/Proton patches in a few seconds.

### Step 3: Deploy to Steam & Configure Launch Options
1. Deploy the patched mod files to your game directory and Proton prefix:
   ```bash
   ./deploy.sh
   ```
   *(Works automatically with standard Steam, Flatpak Steam, or custom game paths set via `SUBNAUTICA_GAME_DIR`)*

2. In Steam:
   - Right-click **Subnautica: Below Zero** -> **Properties**.
   - Under **Compatibility**, check **Force the use of a specific Steam Play compatibility tool** and select **Proton Experimental** (or **Proton 9** / **GE-Proton**).
   - Under **General**, set **Launch Options** to:
     ```bash
     eval $(echo "%command%" | sed "s/SubnauticaZero.exe/Subnautica.Multiplayer.exe/")
     ```
3. Click **Play** in Steam!

---

## In-Game Multiplayer: Hosting & Joining

### To Host a Game:
1. In the main menu, click **Multiplayer**.
2. Click **Host Game**.
3. Select an existing world or click **New Game** (Survival, Freedom, Creative).
4. The local server starts immediately on UDP port `666` and your client connects to `127.0.0.1:666`.

### For Friends to Join (LAN or Tailscale):
Friends on either **Linux** or **Windows** can join your game. Windows players do not need Linux patches — they can use the standard mod.

1. Connect both machines to the same local network or **Tailscale** mesh network.
2. Get the host machine's IP (e.g. `100.x.y.z` from Tailscale or `192.168.x.y` from LAN).
3. On the friend's game:
   - Click **Multiplayer** -> **Join Server**.
   - Click **Add Server**.
   - Enter a Name and the Host's IP address.
   - Click **Connect** / **Join**.

---

## Technical Details: What Was Patched & Why

The official Windows launcher (`Subnautica.Multiplayer.exe`) and its engine assembly (`Subnautica.API.dll`) were patched using [Mono.Cecil](https://github.com/jbevain/cecil) to remove Windows platform dependencies while preserving 100% of the original WPF UI, asset downers, settings, and game-hooking system:

1. **Steam & Wine/Proton Path Resolution**:
   - Skips the Windows registry lookup for Steam (`Software\Valve\Steam`).
   - Resolves the game install directory inside Proton using environment variables (`STEAM_COMPAT_INSTALL_PATH`, `SUBNAUTICA_ZERO_PATH`), current directory, or Wine's mapped `Z:\` drive paths.
   - Executes `SubnauticaZero.exe` directly inside the Proton container instead of triggering `steam://rungameid/848450` (which would loop back into the launcher).
2. **NetBird & Windows Firewall COM Removal**:
   - Stubs out the bundled Windows `netbird.exe` installer and background service.
   - Stubs out Windows Firewall COM policy (`HNetCfg.FwPolicy2`) checks.
3. **Instant LAN / Tailscale Server Creation & Version Sync**:
   - Patches `InviteCode` async state machines (`<CreateServerAsync>d__50`, `<JoinServerAsync>d__49`, `<LeaveFromServerAsync>d__51`, `<PingToServerAsync>d__52`) to connect directly via IP and use standard ECMA-335 generic delegate invocations (`Action`1::Invoke(!0)`), preventing Mono `MissingMethodException` crashes.
   - Stubs `Tools.GetLauncherVersion` and the assembly identity to report version `3.0.1` during client-server connection handshakes, guaranteeing 100% protocol compatibility between players.
4. **Update Protection & Clean Backup**:
   - Mod dependency downloader filters out Windows firewall/NetBird binaries.
   - Injects the patched `Subnautica.API.dll` into the game mod directory on launcher startup and dependency download.
   - Creates an `Assembly-CSharp.dll.orig` backup before hooking game files.
5. **TLS/SSL & Offline Resilience**:
   - Configures `ServicePointManager` to TLS 1.2/1.3 and bypasses Wine's missing root CA certificate validation.
   - Pre-seeds official mod metadata (`Api.json` and `credits.json`) directly into the Proton AppData folder on startup.
   - Suppresses blocking connection error popups during version check and image downloading, guaranteeing the launcher always opens smoothly even when offline or behind firewalls.

---

## Updating or Re-patching

To re-download and re-patch a fresh official executable at any time:
```bash
./patch.sh --download
./deploy.sh
```

---

## Restoring to Vanilla Singleplayer

To revert back to the unmodded game at any time:
1. In the game folder `SubnauticaZero_Data/Managed/`, restore `Assembly-CSharp.dll.orig` over `Assembly-CSharp.dll`.
2. Clear the Steam Launch Options in the game's properties window.
*(Alternatively, in Steam: right-click **Subnautica: Below Zero** -> **Properties** -> **Installed Files** -> **Verify integrity of game files**).*

---

## Mac

> [!NOTE] 
> If running on a Mac, by default you will not be able to host a server. This is because all ports below 1024 are restricted. It is however possible to setup a packet filter rule to redirect the traffic and bypass this limitation.

please refer to the [Mac Guide](MAC_GUIDE.md)

Keep in mind this patch has not been tested on macOS, but it should work.
---

Please Note that a *lot* of help from gemini was used in the making of this.
