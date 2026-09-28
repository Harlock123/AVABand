#!/usr/bin/env bash
# Writes a save with the AVABand of a given commit, for tests/Angband.Tests/Saves (OldSaveTests):
# a seed-7 mage jumped to 150 ft, walked about, then saved. Usage: tools/old-saves/make-old-save.sh <commit>
set -euo pipefail
commit=${1:?usage: make-old-save.sh <commit>}
repo=$(git rev-parse --show-toplevel)
work=$(mktemp -d)
trap 'git -C "$repo" worktree remove --force "$work/tree" >/dev/null 2>&1 || true; rm -rf "$work"' EXIT
git -C "$repo" worktree add --detach "$work/tree" "$commit" >/dev/null
cp "$repo/tools/old-saves/MakeFixtureSave.cs.txt" "$work/tree/tests/Angband.Tests/MakeFixtureSave.cs"
short=$(git -C "$repo" rev-parse --short=7 "$commit")
(cd "$work/tree" && FIXTURE_OUT="$repo/tests/Angband.Tests/Saves/$short.avasave" \
    dotnet test tests/Angband.Tests --filter "FullyQualifiedName~MakeFixtureSave" --nologo -v q)
cat "$repo/tests/Angband.Tests/Saves/$short.avasave.txt"; echo
rm "$repo/tests/Angband.Tests/Saves/$short.avasave.txt"
echo "Wrote tests/Angband.Tests/Saves/$short.avasave"
