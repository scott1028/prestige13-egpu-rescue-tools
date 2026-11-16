#!/bin/bash
# Install the NVIDIA open driver and BAR1 fix for the GC34X eGPU, then test on the live system.
# Usage: sudo bash install.sh
set -euo pipefail

[ "$(id -u)" = 0 ] || { echo "Run with sudo."; exit 1; }
here=$(dirname "$(readlink -f "$0")")
exec > >(tee "$here/install.log") 2>&1

addr=$(lspci -D -d 10de:2c18 | cut -d' ' -f1)
[ -n "$addr" ] || { echo "eGPU not found. Plug in the GC34X and retry."; exit 1; }

echo "== 1/4 Install NVIDIA driver (also blacklists nouveau)"
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y \
  nvidia-driver-595-open linux-modules-nvidia-595-open-generic-hwe-26.04

echo "== 2/4 Install BAR1 fix (udev rule + helper)"
install -m 0755 "$here/egpu-bar1" /usr/local/sbin/egpu-bar1
install -m 0644 "$here/70-egpu-bar1.rules" /etc/udev/rules.d/70-egpu-bar1.rules
udevadm control --reload

set +e # from here on, keep printing diagnostics even if a step fails
echo "== 3/4 Apply BAR1 fix to $addr"
# nouveau is loaded but unbound after its failed probe; unload it so it cannot grab the GPU.
if [ -d /sys/module/nouveau ]; then modprobe -r nouveau; fi
/usr/local/sbin/egpu-bar1 "$addr"
udevadm settle

echo "== 4/4 Check"
addr=$(lspci -D -d 10de:2c18 | cut -d' ' -f1)
lspci -vv -s "$addr" | grep -E "Region [13]|BAR 1:|Kernel driver"
ls "/sys/bus/pci/devices/$addr/drm"
nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv
nvidia-smi -q -d MEMORY | grep -A1 BAR1
journalctl -t egpu-bar1 -b --no-pager | tail -5
journalctl -k -b --no-pager --since "-10min" | grep -E "NVRM|nvidia|$addr" | tail -25
echo "Done. Log: $here/install.log"
