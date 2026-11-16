#!/bin/bash
# Build egpu-mode-x86_64.AppImage from tools/egpu-linux/egpu-mode.
# Usage: build-appimage.sh [output-dir]   (default: dist/ at the repo root)
# Needs python3 with venv and internet access: it downloads python-appimage,
# a Python base AppImage, appimagetool and PyQt6.
set -euo pipefail

here=$(dirname "$(readlink -f "$0")")
out=$(realpath -m "${1:-$here/../../../dist}")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

python3 -m venv "$work/venv"
"$work/venv/bin/pip" install --quiet python-appimage==1.4.7

# Builds the AppDir egpu-mode-x86_64/. The desktop file's Name= sets that name.
# --extra-data takes several values, so it goes last.
cd "$work"
"$work/venv/bin/python-appimage" build app "$here/egpu-mode" --python-version 3.12 \
    --no-packaging --extra-data "$here/../egpu-mode"

# The base image's .DirIcon still points to its removed python.png.
ln -sfn egpu-mode.png egpu-mode-x86_64/.DirIcon
"$work/venv/bin/python" -c 'from python_appimage.appimage import build_appimage
build_appimage("egpu-mode-x86_64", destination="egpu-mode-x86_64.AppImage")'

mkdir -p "$out"
mv egpu-mode-x86_64.AppImage "$out/"
echo "Built $out/egpu-mode-x86_64.AppImage"
