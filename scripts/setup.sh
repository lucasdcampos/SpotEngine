#!/usr/bin/env bash
# Compile the whole engine and put the 'spot' CLI on PATH via a ~/.local/bin symlink.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"

# Compile everything first.
bash "$SCRIPT_DIR/build.sh"

APPHOST="$REPO/bin/Spot.Cli/Release/net10.0/spot"
if [ ! -f "$APPHOST" ]; then
    echo "error: the 'spot' apphost was not found at '$APPHOST' after the build." >&2
    exit 1
fi
chmod +x "$APPHOST"

BINDIR="$HOME/.local/bin"
mkdir -p "$BINDIR"
ln -sf "$APPHOST" "$BINDIR/spot"
echo "Linked $BINDIR/spot -> $APPHOST"

# .NET resolves the app's DLLs through the symlink, so running 'spot' from anywhere works.
case ":$PATH:" in
    *":$BINDIR:"*) ;;
    *)
        echo
        echo "note: $BINDIR is not on your PATH. Add this to your shell profile (~/.bashrc, ~/.zshrc, ...):"
        echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
        ;;
esac

echo
echo "Done. Run:  spot help"
