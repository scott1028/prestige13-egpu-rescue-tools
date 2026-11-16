#! /bin/bash
# AppImage entry point (a python-appimage template). -I ignores PYTHON* variables and user site-packages.
exec {{ python-executable }} -I "${APPDIR}/egpu-mode" "$@"
