#!/usr/bin/env bash
# Remove the 'spot' CLI symlink from ~/.local/bin. Leaves build output untouched.
set -euo pipefail

LINK="$HOME/.local/bin/spot"
if [ -L "$LINK" ] || [ -e "$LINK" ]; then
    rm -f "$LINK"
    echo "Removed $LINK"
else
    echo "Nothing to remove: $LINK does not exist."
fi
