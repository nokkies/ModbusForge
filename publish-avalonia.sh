#!/usr/bin/env bash
# Publishes ModbusForge.Avalonia as self-contained single-file executables
# for Windows x64 and Linux x64.

set -e

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$REPO_ROOT/ModbusForge.Avalonia/ModbusForge.Avalonia.csproj"

RUNTIME="${1:-all}"
PROFILES=()

if [ "$RUNTIME" = "win-x64" ] || [ "$RUNTIME" = "all" ]; then
    PROFILES+=("win-x64")
fi

if [ "$RUNTIME" = "linux-x64" ] || [ "$RUNTIME" = "all" ]; then
    PROFILES+=("linux-x64")
fi

for PROFILE in "${PROFILES[@]}"; do
    PROFILE_PATH="ModbusForge.Avalonia/Properties/PublishProfiles/$PROFILE.pubxml"
    FULL_PROFILE="$REPO_ROOT/$PROFILE_PATH"

    if [ ! -f "$FULL_PROFILE" ]; then
        echo "Publish profile not found: $FULL_PROFILE" >&2
        exit 1
    fi

    echo "Publishing ModbusForge.Avalonia for $PROFILE..."
    dotnet publish "$PROJECT" -p:PublishProfile="$PROFILE" -c Release

    PUBLISH_DIR="$REPO_ROOT/publish/avalonia/$PROFILE"
    echo "Published to $PUBLISH_DIR"
done

echo "Done."
