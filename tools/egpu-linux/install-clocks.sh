#!/bin/bash
# Install the eGPU clock lock: sudo bash install-clocks.sh
set -eu
[ "$(id -u)" = 0 ] || { echo "run with sudo"; exit 1; }
src=$(dirname "$(readlink -f "$0")")

install -m 755 "$src/egpu-clocks" /usr/local/sbin/egpu-clocks
install -m 644 "$src/egpu-clocks.service" /etc/systemd/system/egpu-clocks.service
install -m 644 "$src/72-egpu-clocks.rules" /etc/udev/rules.d/72-egpu-clocks.rules
systemctl daemon-reload
udevadm control --reload-rules

gpu=$(lspci -D -d 10de:2c18 | awk '{print $1; exit}')
[ -n "$gpu" ] || { echo "Installed. eGPU not attached; it applies on next plug-in."; exit 0; }

# Verify end to end: drop the manual locks, then replay the bind event through udev.
nvidia-smi -rgc
nvidia-smi -rmc
since=$(date '+%F %T')
udevadm trigger --action=bind "/sys/bus/pci/devices/$gpu"
for _ in $(seq 30); do # wait up to 15 s for the service to log
  log=$(journalctl -t egpu-clocks --since "$since" --no-pager -o cat)
  [ -z "$log" ] || break
  sleep 0.5
done
echo "egpu-clocks: ${log:-no log after 15 s, check: systemctl status egpu-clocks.service}"
