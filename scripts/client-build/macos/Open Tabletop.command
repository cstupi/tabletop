#!/bin/sh
# Ships beside Tabletop.app in the macOS zip. macOS marks every file of a downloaded, un-notarised app as quarantined;
# "Open Anyway" clears the app but not the .NET libraries inside it, and the client then starts without its code.
# This clears the mark from the whole bundle and opens it.
cd "$(dirname "$0")" || exit 1
app="Tabletop.app"
[ -d "$app" ] || app="/Applications/Tabletop.app"
if [ ! -d "$app" ]; then
  echo "Tabletop.app is not beside this file or in Applications."
  exit 1
fi
xattr -dr com.apple.quarantine "$app" && open "$app"
