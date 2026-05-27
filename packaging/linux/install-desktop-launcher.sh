#!/usr/bin/env sh
set -eu

app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
app_path="$app_dir/eq2emu-questparser"
desktop_path="$HOME/.local/share/applications/eq2emu-questparser.desktop"

if [ ! -f "$app_path" ]; then
    echo "Cannot find eq2emu-questparser beside this installer." >&2
    exit 1
fi

chmod +x "$app_path" 2>/dev/null || true

mkdir -p "$(dirname -- "$desktop_path")"

escaped_app_path=$(printf '%s' "$app_path" | sed 's/\\/\\\\/g; s/"/\\"/g')

cat > "$desktop_path" <<EOF
[Desktop Entry]
Type=Application
Name=EQ2Emu QuestParser
Comment=EQ2Emu quest authoring tool
Exec="$escaped_app_path"
Icon=applications-development
Terminal=false
Categories=Development;Utility;
StartupNotify=true
EOF

chmod +x "$desktop_path" 2>/dev/null || true

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true
fi

echo "Installed desktop launcher:"
echo "  $desktop_path"
echo "Executable:"
echo "  $app_path"
