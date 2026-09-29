#!/usr/bin/env python3
"""Compose and render AVABand's title music, "The Gates of Angband" (CC0, made for AVABand).

Everything is synthesised here - no samples: an organ drone, tolling bells, wind, a string
ensemble, a plucked harp, a horn, a wordless choir and timpani, in a synthetic hall. D minor,
about two minutes, starting and ending on the drone so it loops.

Needs numpy, scipy and soundfile (pip install numpy scipy soundfile).

Usage:
    title_theme.py <out.ogg> [--wav preview.wav]
"""
import argparse

import numpy as np
import soundfile as sf
from scipy import signal

SR = 44100
BPM = 64
BEAT = 60.0 / BPM
BAR = 4 * BEAT
RNG = np.random.default_rng(1983)  # Angband's own vintage


def hz(note):
    """'D4', 'C#5', 'Bb3' -> frequency."""
    names = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}
    n = names[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    midi = 12 * (int(rest) + 1) + n
    return 440.0 * 2 ** ((midi - 69) / 12)


def env(n, attack, release, sustain=1.0, decay=0.0):
    """A linear-attack, exponential-ish release envelope over n samples."""
    t = np.arange(n) / SR
    e = np.ones(n) * sustain
    a = max(1, int(attack * SR))
    e[:a] = np.linspace(0, 1, a)
    if decay > 0:
        d = int(decay * SR)
        e[a:a + d] = np.linspace(1, sustain, len(e[a:a + d]))
    r = max(1, int(release * SR))
    if r < n:
        e[-r:] *= np.linspace(1, 0, r) ** 2
    return e


def saw(freq, n, detune_cents=0.0, vib_rate=0.0, vib_depth=0.0, phase=None):
    """A band-limited-ish sawtooth (additive, harmonics under 12 kHz) with optional vibrato."""
    t = np.arange(n) / SR
    f = freq * 2 ** (detune_cents / 1200)
    if vib_rate:
        inst = f * (1 + vib_depth * np.sin(2 * np.pi * vib_rate * t + RNG.uniform(0, 6)))
        ph = 2 * np.pi * np.cumsum(inst) / SR
    else:
        ph = 2 * np.pi * f * t
    ph += RNG.uniform(0, 2 * np.pi) if phase is None else phase
    out = np.zeros(n)
    k = 1
    while k * f < 12000 and k <= 40:
        out += np.sin(k * ph) / k
        k += 1
    return out * 0.6


def lowpass(x, cutoff, order=2):
    b, a = signal.butter(order, min(cutoff, SR / 2 - 100) / (SR / 2), "low")
    return signal.lfilter(b, a, x)


def bandpass(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], "band")
    return signal.lfilter(b, a, x)


# --- Instruments ---------------------------------------------------------------------------------

def strings(freq, dur):
    n = int((dur + 1.2) * SR)
    x = sum(saw(freq, n, c, 5.2, 0.004) for c in (-9, -3, 4, 10))
    x = lowpass(x, 1700, 4)  # warm, not buzzy
    return x * env(n, 0.9, 1.4) * 0.16


def choir(freq, dur):
    n = int((dur + 1.4) * SR)
    src = sum(saw(freq, n, c, 5.0, 0.006) for c in (-12, 0, 11))
    # an "ah": formants at 800, 1150 and 2900 Hz
    x = 1.0 * bandpass(src, 650, 950) + 0.55 * bandpass(src, 1000, 1300) + 0.18 * bandpass(src, 2600, 3200)
    return x * env(n, 1.2, 1.6) * 0.5


def horn(freq, dur):
    n = int((dur + 0.5) * SR)
    x = saw(freq, n, 0, 4.6, 0.005) + 0.5 * saw(freq, n, 6, 4.6, 0.005)
    # brassy: the filter opens with the attack
    t = np.arange(n) / SR
    bright = np.clip(t / 0.12, 0, 1)
    x = lowpass(x, 800, 4) * (1 - bright * 0.5) + lowpass(x, 2000, 4) * bright * 0.5
    return x * env(n, 0.07, 0.45, 0.85, 0.25) * 0.42


def harp(freq, dur):
    """Karplus-Strong pluck."""
    n = int((dur + 2.5) * SR)
    period = int(SR / freq)
    burst = np.zeros(n)
    burst[:period] = lowpass(RNG.uniform(-1, 1, period), 3000, 1)
    # y[i] = x[i] + 0.4985 (y[i-P] + y[i-P-1]): the string's averaging delay line
    a = np.zeros(period + 2)
    a[0], a[period], a[period + 1] = 1.0, -0.4985, -0.4985
    out = signal.lfilter([1.0], a, burst)
    return out * env(n, 0.002, 0.3) * 0.5


def bell(freq, dur=6.0):
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for ratio, amp, decay in ((0.5, 0.6, 2.8), (1.0, 1.0, 2.2), (1.19, 0.5, 1.6), (1.56, 0.4, 1.2),
                              (2.0, 0.35, 1.0), (2.74, 0.25, 0.7), (3.76, 0.15, 0.5)):
        x += amp * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t / decay)
    return x * env(n, 0.003, 0.5) * 0.22


def timpani(freq, loud=1.0):
    n = int(2.2 * SR)
    t = np.arange(n) / SR
    f = freq * (1 + 0.08 * np.exp(-t / 0.05))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.55)
    x += 0.5 * np.sin(2 * np.pi * np.cumsum(f * 1.5) / SR) * np.exp(-t / 0.3)
    x += lowpass(RNG.standard_normal(n), 400) * np.exp(-t / 0.04) * 0.6
    return x * 0.5 * loud


def drone(dur):
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for f, a in ((hz("D2"), 1.0), (hz("A2"), 0.45), (hz("D3"), 0.35), (hz("D1"), 0.6)):
        for h, ha in ((1, 1.0), (2, 0.3), (3, 0.15), (4, 0.08)):
            x += a * ha * np.sin(2 * np.pi * f * h * t + RNG.uniform(0, 6))
    swell = 0.75 + 0.25 * np.sin(2 * np.pi * t / 9.0)
    return lowpass(x, 1200) * swell * 0.1


def wind(dur):
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = RNG.standard_normal(n)
    # a slowly moving band of noise
    out = np.zeros(n)
    seg = SR // 2
    for s in range(0, n, seg):
        c = 600 + 300 * np.sin(2 * np.pi * s / SR / 13.0) + 150 * np.sin(2 * np.pi * s / SR / 5.3)
        chunk = x[max(0, s - 2048):s + seg]
        out[s:s + seg] = bandpass(chunk, c * 0.7, c * 1.4)[-len(out[s:s + seg]):]
    gust = 0.5 + 0.5 * np.sin(2 * np.pi * t / 11.0) ** 2
    return out * gust * 0.05


# --- The score -----------------------------------------------------------------------------------

CHORDS = {  # voicings for strings/choir
    "Dm": ["D3", "A3", "D4", "F4"], "Bb": ["Bb2", "F3", "Bb3", "D4"], "Gm": ["G2", "D3", "G3", "Bb3"],
    "A": ["A2", "E3", "A3", "C#4"], "F": ["F2", "C3", "F3", "A3"], "C": ["C3", "G3", "C4", "E4"],
    "Gm/D": ["D3", "G3", "Bb3", "D4"], "A7": ["A2", "E3", "G3", "C#4"],
}
PROGRESSION = ["Dm", "Bb", "Gm", "A", "Dm", "F", "Gm", "A"]
# The theme: (note, beats); None is a rest.
THEME = [
    ("D5", 2), ("A4", 1), ("D5", 1),
    ("F5", 3), ("E5", 0.5), ("D5", 0.5),
    ("D5", 2), ("C5", 1), ("Bb4", 1),
    ("A4", 2), ("C#5", 1), ("E5", 1),
    ("F5", 2), ("E5", 1), ("D5", 1),
    ("C5", 2), ("A4", 1), ("C5", 1),
    ("Bb4", 1.5), ("A4", 0.5), ("G4", 1), ("A4", 1),
    ("D5", 4),
]
HARP_PATTERN = [0, 1, 2, 3, 2, 1, 2, 3]  # indexes into the chord, in eighths


def render():
    intro, a_bars, b_bars, a2_bars, outro = 2, 8, 8, 8, 2
    total_bars = intro + a_bars + b_bars + a2_bars + outro
    length = total_bars * BAR + 6.0
    n = int(length * SR)
    L = np.zeros(n)
    R = np.zeros(n)

    def put(x, at, pan=0.0, gain=1.0):
        i = int(at * SR)
        if i >= n:
            return
        x = x[: n - i] * gain
        L[i:i + len(x)] += x * np.sqrt(0.5 * (1 - pan))
        R[i:i + len(x)] += x * np.sqrt(0.5 * (1 + pan))

    # Drone and wind under everything (fading at the very ends so the loop meets itself).
    d = drone(length)
    put(d, 0, -0.1)
    put(wind(length), 0, 0.3)
    put(wind(length), 0.37, -0.4)

    # Bells toll through the intro and outro, and mark each section.
    for bar in list(range(0, intro)) + list(range(total_bars - outro, total_bars)):
        put(bell(hz("D4")), bar * BAR, 0.35, 1.0)
        if bar % 2 == 1:
            put(bell(hz("A3")), bar * BAR + 2 * BEAT, -0.35, 0.7)
    for bar in (intro, intro + a_bars, intro + a_bars + b_bars):
        put(bell(hz("D5")), bar * BAR, 0.5, 0.6)

    start_a = intro * BAR
    start_b = start_a + a_bars * BAR
    start_a2 = start_b + b_bars * BAR
    for section, start, bars in (("A", start_a, a_bars), ("B", start_b, b_bars), ("A2", start_a2, a2_bars)):
        for i in range(bars):
            chord = CHORDS[PROGRESSION[i % len(PROGRESSION)]]
            at = start + i * BAR
            # strings hold the chord
            for j, note in enumerate(chord):
                put(strings(hz(note), BAR), at, -0.5 + j / 3, 1.0 if section != "A" else 0.8)
            # harp arpeggios in eighths (an octave up, over the chord)
            if section in ("A", "A2"):
                for k, idx in enumerate(HARP_PATTERN):
                    f = hz(chord[idx]) * (2 if section == "A" else 4 if idx < 2 else 2)
                    put(harp(f, BEAT / 2), at + k * BEAT / 2, 0.45, 0.8 if section == "A" else 0.55)
            # choir from B on
            if section in ("B", "A2"):
                for j, note in enumerate(chord[1:]):
                    put(choir(hz(note) * 2, BAR), at, 0.4 - j * 0.4, 0.8 if section == "B" else 1.0)
            # timpani: the downbeat of each bar in B; rolls into the cadences in A2
            if section == "B" or (section == "A2" and i % 2 == 0):
                put(timpani(hz("D2") if chord[0][0] in "DBF" else hz("A1")), at, 0.0, 0.9)
            if section == "A2" and i in (3, 7):
                for k in range(8):
                    put(timpani(hz("A1"), 0.25 + k * 0.06), at + 2 * BEAT + k * BEAT / 4, 0.0)

        # the horn sings the theme in B; in A2 an octave down with the choir above
        if section in ("B", "A2"):
            beat = 0.0
            for note, beats in THEME:
                if note is not None:
                    f = hz(note) / (2 if section == "A2" else 1)
                    put(horn(f, beats * BEAT), start + beat * BEAT, -0.15, 1.0)
                    if section == "A2":
                        put(choir(hz(note), beats * BEAT), start + beat * BEAT, 0.2, 0.55)
                beat += beats

    # The hall: a synthetic stereo impulse response, 3.4 s.
    ir_n = int(3.4 * SR)
    t = np.arange(ir_n) / SR
    decay = np.exp(-t / 0.75)
    irl = RNG.standard_normal(ir_n) * decay
    irr = RNG.standard_normal(ir_n) * decay
    irl = lowpass(irl, 5000)
    irr = lowpass(irr, 5000)
    irl[:int(0.02 * SR)] = 0
    irr[:int(0.023 * SR)] = 0
    wet_l = signal.fftconvolve(L, irl)[:n]
    wet_r = signal.fftconvolve(R, irr)[:n]
    wet_scale = 0.9 * np.sqrt(np.mean(L ** 2) / max(np.mean(wet_l ** 2), 1e-12))
    L = L * 0.75 + wet_l * wet_scale * 0.45
    R = R * 0.75 + wet_r * wet_scale * 0.45

    # Fade in and out over the drone so the loop is seamless; normalise to -1 dBFS.
    fade = int(2.5 * SR)
    ramp = np.linspace(0, 1, fade) ** 2
    for ch in (L, R):
        ch[:fade] *= ramp
        ch[-fade:] *= ramp[::-1]
    stereo = np.stack([L, R], axis=1)
    stereo /= np.max(np.abs(stereo)) / 10 ** (-1 / 20)
    return stereo


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--wav")
    a = ap.parse_args()
    audio = render().astype(np.float32)
    if a.wav:
        sf.write(a.wav, audio, SR)
    # (libsndfile's Vorbis encoder is happier fed in blocks than all at once)
    with sf.SoundFile(a.out, "w", SR, 2, format="OGG", subtype="VORBIS") as f:
        for i in range(0, len(audio), 16384):
            f.write(audio[i:i + 16384])
    print(f"{a.out}: {len(audio) / SR:.1f} s")


if __name__ == "__main__":
    main()
