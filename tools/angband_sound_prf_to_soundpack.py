#!/usr/bin/env python3
"""Convert Angband 4.2's lib/customize/sound.prf into an AVABand soundpack.json.

sound.prf lines look like `sound:HIT:plc_hit_hay plc_hit_body` (event name, then sound file stems).
AVABand uses the same event names, so this is a direct mapping to "<stem>.mp3" files.

Usage: angband_sound_prf_to_soundpack.py sound.prf <sound folder> out/soundpack.json
"""
import json, os, sys

prf, folder, out = sys.argv[1:4]
sounds, missing = {}, []
for line in open(prf, encoding="utf-8"):
    line = line.strip()
    if not line.startswith("sound:"):
        continue
    _, event, stems = line.split(":", 2)
    files = []
    for stem in stems.split():
        name = stem + ".mp3"
        (files if os.path.exists(os.path.join(folder, name)) else missing).append(name)
    if files:
        sounds[event] = files

manifest = {
    "name": "Angband sounds (Dubtrain)",
    "author": "Dubtrain",
    "license": "Creative Commons Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/)",
    "source": "https://github.com/angband/angband/tree/master/lib/sounds",
    "sounds": sounds,
    # Ambient cues and the death sound are a little loud against the effects.
    "volumes": {"AMBIENT_DAY": 0.5, "AMBIENT_NITE": 0.5, **{f"AMBIENT_DNG{i}": 0.5 for i in range(1, 6)}},
}
json.dump(manifest, open(out, "w"), indent=1)
open(out, "a").write("\n")
print(f"{out}: {len(sounds)} events, {sum(len(v) for v in sounds.values())} files; missing: {missing or 'none'}")
