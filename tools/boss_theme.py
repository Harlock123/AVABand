#!/usr/bin/env python3
"""Compose and render AVABand's boss music, "The Foe Before You" (CC0, made for AVABand).

Played while a great foe is in view (a unique from 500 ft down, or a quest's own). Everything is
synthesised, with the title music's instruments (title_theme.py): a driving ostinato in the low
strings, timpani and a snare on the off-beats, stabbing horns, and a choir that comes in as it
builds. D phrygian, 132 bpm, 32 bars (about a minute), starting and ending on the ostinato's D so
it follows itself round.

Usage: boss_theme.py <out.ogg>
"""
import os
import sys

import numpy as np
import soundfile as sf
from scipy import signal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import title_theme as T  # noqa: E402

SR = T.SR
BPM = 132
BEAT = 60.0 / BPM
BAR = 4 * BEAT
BARS = 32
RNG = np.random.default_rng(666)


def place(buf, sound, at, gain=1.0, pan=0.0):
    i = int(at * SR)
    end = min(buf.shape[0], i + len(sound))
    if end <= i:
        return
    left, right = np.sqrt(0.5 * (1 - pan)), np.sqrt(0.5 * (1 + pan))
    buf[i:end, 0] += sound[: end - i] * gain * left
    buf[i:end, 1] += sound[: end - i] * gain * right


def stab(freq, dur):
    """A short bowed note, cut off hard: the ostinato."""
    n = int(dur * SR)
    x = T.saw(freq, n, detune_cents=7) + T.saw(freq * 2, n, detune_cents=-5) * 0.4
    e = np.minimum(1, np.arange(n) / (0.006 * SR)) * np.exp(-np.arange(n) / (dur * 0.55 * SR))
    return T.lowpass(x * e, 1400, 2)


def snare():
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    body = np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.03)
    rattle = T.bandpass(RNG.standard_normal(n), 1500, 7000) * np.exp(-t / 0.07)
    return body * 0.5 + rattle * 0.8


def reverb(buf, seconds=1.6, wet=0.28):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    out = np.empty_like(buf)
    for ch in range(2):
        ir = RNG.standard_normal(n) * np.exp(-t / (seconds / 5))
        ir = T.lowpass(ir, 5000)
        ir[: int(0.012 * SR)] = 0
        w = signal.fftconvolve(buf[:, ch], ir)[: len(buf)]
        w *= np.sqrt(np.mean(buf[:, ch] ** 2) / max(np.mean(w ** 2), 1e-12))
        out[:, ch] = buf[:, ch] * (1 - wet) + w * wet
    return out


def render():
    total = BARS * BAR + 3.0
    buf = np.zeros((int(total * SR), 2))
    # The harmony, a bar at a time: D, D, Bb, C — and in the second half, D, Eb, Bb, A (darker).
    roots = (["D2", "D2", "Bb1", "C2"] * 4) + (["D2", "Eb2", "Bb1", "A1"] * 4)
    pattern = [0, 0, 0, 1, 0, 0, -2, 0]                       # semitones from the root, in eighths
    for bar, root in enumerate(roots):
        start = bar * BAR
        base = T.hz(root)
        for k, step in enumerate(pattern):
            at = start + k * BEAT / 2
            f = base * 2 ** (step / 12)
            place(buf, stab(f, BEAT * 0.45), at, 0.55, -0.25)
            place(buf, stab(f * 2, BEAT * 0.4), at, 0.25, 0.25)
        # Timpani on 1 and 3; a roll into every fourth bar.
        place(buf, T.timpani(base * 2, 1.0), start, 0.9)
        place(buf, T.timpani(base * 2 * 1.5, 0.7), start + 2 * BEAT, 0.7)
        if bar % 4 == 3:
            for r in range(6):
                place(buf, T.timpani(base * 2, 0.4 + r * 0.1), start + 3 * BEAT + r * BEAT / 6, 0.5)
        # The snare on 2 and 4, from the fifth bar.
        if bar >= 4:
            for b in (1, 3):
                place(buf, snare(), start + b * BEAT, 0.35, 0.1)
        # Horns stab the off-beats from bar 9: the root's minor chord, with the flat second on top in the second half.
        if bar >= 8 and bar % 2 == 0:
            chord = [base * 4, base * 4 * 2 ** (3 / 12), base * 4 * 2 ** (7 / 12)]
            if bar >= 16:
                chord.append(base * 8 * 2 ** (1 / 12))
            for f in chord:
                place(buf, T.horn(f, BEAT * 0.9), start + 1.5 * BEAT, 0.16)
                place(buf, T.horn(f, BEAT * 1.6), start + 3.5 * BEAT, 0.14)
        # The choir comes in for the second half: long chords, a bar each.
        if bar >= 16:
            for f in (base * 4, base * 4 * 2 ** (3 / 12), base * 4 * 2 ** (7 / 12)):
                place(buf, T.choir(f, BAR * 1.05), start, 0.09)
    # The last bars: the strings climb, and a final hit.
    end = BARS * BAR
    for k in range(16):
        f = T.hz("D3") * 2 ** (k / 12)
        place(buf, T.strings(f, BEAT * 0.9), end - 4 * BAR + k * BEAT, 0.05)
    place(buf, T.timpani(T.hz("D3"), 1.3), end, 1.0)
    place(buf, stab(T.hz("D2"), 1.5), end, 0.6)
    return reverb(buf)


def main():
    out = sys.argv[1]
    audio = render()
    audio -= audio.mean(axis=0)
    audio *= 10 ** (-18 / 20) / np.sqrt(np.mean(audio ** 2))
    peak = np.max(np.abs(audio))
    if peak > 10 ** (-1 / 20):
        audio *= 10 ** (-1 / 20) / peak
    k = int(0.5 * SR)
    audio[-k:] *= np.linspace(1, 0, k)[:, None]
    audio = audio.astype(np.float32)
    with sf.SoundFile(out, "w", SR, 2, format="OGG", subtype="VORBIS") as f:
        for i in range(0, len(audio), 16384):
            f.write(audio[i:i + 16384])
    print(f"{out}: {len(audio) / SR:.1f} s")


if __name__ == "__main__":
    main()
