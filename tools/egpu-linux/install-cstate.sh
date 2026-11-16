#!/bin/bash
# Install the eGPU C-state limit: sudo bash install-cstate.sh
set -eu
[ "$(id -u)" = 0 ] || { echo "run with sudo"; exit 1; }
src=$(dirname "$(readlink -f "$0")")

install -m 755 "$src/egpu-cstate" /usr/local/sbin/egpu-cstate
install -m 644 "$src/71-egpu-cstate.rules" /etc/udev/rules.d/71-egpu-cstate.rules
udevadm control --reload-rules

# Apply now to match the current attach state.
if lspci -d 10de:2c18 | grep -q .; then
  /usr/local/sbin/egpu-cstate limit
else
  /usr/local/sbin/egpu-cstate restore
fi
grep -H . /sys/devices/system/cpu/cpu0/cpuidle/state*/disable
echo "Installed. Test: unplug the eGPU (expect disable:0 for state2/3), then plug it back (expect 1)."
