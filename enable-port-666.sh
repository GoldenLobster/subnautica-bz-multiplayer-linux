#!/usr/bin/env bash
set -e

echo "=================================================="
echo "Subnautica Below Zero Multiplayer: Port 666 Setup"
echo "=================================================="
echo "The mod server listens on UDP port 666."
echo "On Linux, ports below 1024 require unprivileged access."
echo ""
echo "Setting net.ipv4.ip_unprivileged_port_start=666..."

# Apply immediately for current session
sudo sysctl -w net.ipv4.ip_unprivileged_port_start=666

# Persist across system reboots
echo "net.ipv4.ip_unprivileged_port_start=666" | sudo tee /etc/sysctl.d/99-subnautica-mp.conf > /dev/null

echo ""
echo "Success! Current kernel setting:"
cat /proc/sys/net/ipv4/ip_unprivileged_port_start
echo ""
echo "You can now host Subnautica: Below Zero multiplayer servers!"
