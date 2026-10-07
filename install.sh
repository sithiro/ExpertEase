#!/usr/bin/env bash
set -euo pipefail

# ExpertEase Installer (Linux/macOS)
# Downloads the latest ExpertEase MCP server (with bundled knowledge bases) and prints registration commands.
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/sithiro/ExpertEase/main/install.sh | bash
#   VERSION=1.0.3 bash install.sh      # pin a specific version

REPO="sithiro/ExpertEase"

OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Linux*)   RID="linux-x64" ;;
  Darwin*)
    case "$ARCH" in
      arm64|aarch64) RID="osx-arm64" ;;
      *) echo "Intel Macs are not published; build from source instead (see README)."; exit 1 ;;
    esac
    ;;
  *)
    echo "Unsupported OS: $OS"
    echo "For Windows, use: powershell -c \"irm https://raw.githubusercontent.com/${REPO}/main/install.ps1 | iex\""
    exit 1
    ;;
esac

if [ -n "${VERSION:-}" ]; then
  TAG="expertease-v${VERSION}"
else
  echo "Checking latest version..."
  TAG=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | grep -o '"tag_name":\s*"[^"]*"' | head -1 | cut -d'"' -f4)
  if [ -z "$TAG" ]; then
    echo "Failed to fetch latest release from GitHub."
    exit 1
  fi
fi

VERSION="${TAG#expertease-v}"
URL="https://github.com/${REPO}/releases/download/${TAG}/expertease-${RID}.mcpb"
INSTALL_DIR="ExpertEase v${VERSION}"

echo "Downloading ExpertEase v${VERSION} for ${RID}..."
TMPFILE="$(mktemp)"
trap 'rm -f "$TMPFILE"' EXIT

if command -v curl &>/dev/null; then
  curl -fSL "$URL" -o "$TMPFILE"
elif command -v wget &>/dev/null; then
  wget -q "$URL" -O "$TMPFILE"
else
  echo "Error: curl or wget is required"
  exit 1
fi

echo "Extracting to '${INSTALL_DIR}'..."
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
unzip -o -q "$TMPFILE" "server/*" -d "$INSTALL_DIR"

# Flatten server/ so the binary and ExpertEase.Knowledge/ sit side by side.
mv "$INSTALL_DIR"/server/* "$INSTALL_DIR/"
rmdir "$INSTALL_DIR/server"
chmod +x "$INSTALL_DIR/expertease"

# macOS: ad-hoc sign to satisfy Gatekeeper (binary is not notarized)
if [ "$OS" = "Darwin" ]; then
  codesign --force --deep --sign - "$INSTALL_DIR/expertease" 2>/dev/null || true
fi

BINARY_PATH="$(cd "$INSTALL_DIR" && pwd)/expertease"

echo ""
echo "ExpertEase v${VERSION} installed to '${INSTALL_DIR}'"
echo ""
echo "Register with your agent:"
echo ""
echo "  # Claude Code"
echo "  claude mcp add -s user -t stdio expertease -- \"$BINARY_PATH\""
echo ""
echo "  # Codex"
echo "  codex mcp add expertease -- \"$BINARY_PATH\""
echo ""
echo "  # Copilot (VS Code)"
echo "  code --add-mcp '{\"name\":\"expertease\",\"command\":\"$BINARY_PATH\",\"args\":[]}'"
echo ""
echo "Add your own knowledge bases by setting EXPERTEASE_KNOWLEDGE_DIR to a folder of .json/.csv files."
echo ""
echo "To unregister:"
echo ""
echo "  claude mcp remove -s user expertease"
echo "  codex mcp remove expertease"
echo ""
echo "Done!"
