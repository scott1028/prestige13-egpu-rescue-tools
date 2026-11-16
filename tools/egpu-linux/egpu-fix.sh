#!/bin/bash
# Apply GC34X eGPU stability fixes. Reboot afterwards.
# Usage: sudo bash egpu-fix.sh
set -euo pipefail

[ "$(id -u)" = 0 ] || { echo "Run with sudo."; exit 1; }
here=$(dirname "$(readlink -f "$0")")
exec > >(tee /var/log/egpu-fix.log) 2>&1 # /var/log survives reboot; the scratchpad does not

echo "== 1/3 Install updated helper"
install -m 0755 "$here/egpu-bar1" /usr/local/sbin/egpu-bar1
udevadm control --reload

echo "== 2/3 Module options: thunderbolt CLx off, NVIDIA link capped at Gen3"
install -m 0644 "$here/egpu-gc34x.conf" /etc/modprobe.d/egpu-gc34x.conf
update-initramfs -u

echo "== 3/3 Current link state (diagnostics only)"
set +e
for d in 0000:00:07.0 $(lspci -D -d 8086:5786 | cut -d' ' -f1) $(lspci -D -d 10de:2c18 | cut -d' ' -f1); do
  echo "-- $d"
  lspci -vvv -s "$d" | grep -E "LnkCap:|LnkCtl:|LnkSta:|LnkCtl2:|DevCtl2:|MaxPayload|L1SubCtl1:|Physical Resizable|BAR 1:"
done
echo "Done. Unplug the eGPU, then reboot."
