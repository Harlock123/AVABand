#!/usr/bin/env bash
# Rebuilds AVABand's copies of Angband 4.2.5's Original (8x8) and Nomad (8x16) tilesets.
# Usage: tools/angband_extra_tilesets.sh <angband-4.2.5 source dir>
# web.png and tree.png in each folder are AVABand's own (CC0): the sets have no web, and no tree creature.
set -euo pipefail
A=${1:?angband source dir}; D=src/Angband.Data/data
build() { # dir code sheet w h name author
  local out=src/Angband.Avalonia/tilesets/angband-$1
  mkdir -p "$out"; cp "$A/lib/tiles/$1/$3" "$out/"
  python3 tools/angband_prf_to_tileset.py "$A/lib/tiles/$1/graf-$2.prf" "$A/lib/tiles/$1/xtra-$2.prf" "$out/tileset.json" \
    --name "$6" --author "$7" --license "GNU GPL v2, as Angband (https://www.gnu.org/licenses/old-licenses/gpl-2.0.html)" \
    --source "https://github.com/angband/angband/tree/4.2.5/lib/tiles/$1" --sheet "$3" --size "$4" "$5" \
    --monsters $D/monsters.json --traps $D/traps.json --objects $D/objects.json --bases $D/object_bases.json \
    --flvr "$A/lib/tiles/$1/flvr-$2.prf" --flavor-txt "$A/lib/gamedata/flavor.txt" --flavors $D/flavors.json \
    --extra trap:web=web.png --extra monster-glyph:l=tree.png
  python3 tools/tileset_glyph_fallbacks.py $D/monsters.json "$out/tileset.json"
}
build old xxx 8x8.png 8 8 "Original Tiles (8x8)" "Lars Haugseth, Dawnmist and the Angband contributors"
build nomad nmd 8x16.png 8 16 "Nomad (8x16)" "Nomad, from Adam Bolt's tiles"
