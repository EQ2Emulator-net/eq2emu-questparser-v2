#!/usr/bin/env sh
set -eu

app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
app_path="$app_dir/eq2emu-questparser"

if [ ! -f "$app_path" ]; then
    echo "Cannot find eq2emu-questparser beside this launcher." >&2
    exit 1
fi

chmod +x "$app_path" 2>/dev/null || true
exec "$app_path" "$@"
