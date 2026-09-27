#!/usr/bin/env bash
# Regenerates the README's screenshots in screenshots/. They are rendered headlessly from the real
# windows (tests/Angband.Avalonia.Tests/ReadmeScreenshots.cs) with fixed seeds, so no desktop is
# captured and the pictures only change when the game's look does.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$root/screenshots"
AVABAND_README_SHOTS="$root/screenshots" dotnet test "$root/tests/Angband.Avalonia.Tests" \
  --filter "FullyQualifiedName~ReadmeScreenshots" --nologo -v quiet
ls -1 "$root/screenshots"
