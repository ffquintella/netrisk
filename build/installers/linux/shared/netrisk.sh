#!/bin/sh
# Launcher for the Flatpak/Snap builds.
#
# The client resolves appsettings.json relative to the working directory, so the launcher
# enters the install directory before exec'ing the binary. Administrators can drop a
# netrisk.ini next to it (section [Server], key Url) to pre-seed the server URL, exactly as
# the Windows MSI does through its SERVERURL property.
set -eu
INSTALL_DIR="${NETRISK_INSTALL_DIR:-/app/lib/netrisk}"
cd "$INSTALL_DIR"
exec "./GUIClient" "$@"
