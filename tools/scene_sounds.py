#!/usr/bin/env python3
"""Synthesise the sounds of AVABand's scenes (CC0, made for AVABand): no samples, only noise and
oscillators. SceneView cues them as a scene plays (Controls/SceneView.cs, SceneView.CuesFor).

  recall-reach   the rent tears open (a ripping crackle), then a rising, shimmering rush as the hand reaches
  recall-take    the hand closes (a deep thump), and a thunderclap as the light takes you
  stairs-down    footsteps going down stone stairs, fainter and more echoing with each
  stairs-up      footsteps coming up, nearer each, into open air
  death          a single deep funeral bell
  unique         an ominous low brass chord, swelling
  danger         a low rumble under a dissonant drone
  fall           stone cracking, a rushing fall, and a heavy landing
  quest-complete a bright rising fanfare of bells over a warm chord
  boss-slain     a great gong, and a low choir-like swell dying away

Needs numpy, scipy and soundfile. Usage: scene_sounds.py <out folder>
"""
import os
import sys

import numpy as np
import soundfile as sf
from scipy import signal

SR = 44100
RNG = np.random.default_rng(99)


def t_of(seconds):
    return np.arange(int(seconds * SR)) / SR


def lowpass(x, cutoff, order=2):
    b, a = signal.butter(order, min(cutoff, SR / 2 - 100) / (SR / 2), "low")
    return signal.lfilter(b, a, x)


def highpass(x, cutoff, order=2):
    b, a = signal.butter(order, cutoff / (SR / 2), "high")
    return signal.lfilter(b, a, x)


def bandpass(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], "band")
    return signal.lfilter(b, a, x)


def env(n, attack, release, total=None):
    """A simple attack/release envelope over n samples (seconds)."""
    e = np.ones(n)
    a, r = int(attack * SR), int(release * SR)
    if a: e[:a] = np.linspace(0, 1, a)
    if r: e[-r:] *= np.linspace(1, 0, r) ** 2
    return e


def reverb(x, seconds, damp=4000, wet=0.4):
    n = int(seconds * SR)
    tt = np.arange(n) / SR
    ir = RNG.standard_normal(n) * np.exp(-tt / (seconds / 5))
    ir = lowpass(ir, damp)
    ir[: int(0.01 * SR)] = 0
    w = signal.fftconvolve(x, ir)[: len(x)]
    w *= np.sqrt(np.mean(x ** 2) / max(np.mean(w ** 2), 1e-12))
    return x * (1 - wet) + w * wet


def place(buf, sound, at, gain=1.0):
    i = int(at * SR)
    end = min(len(buf), i + len(sound))
    buf[i:end] += sound[: end - i] * gain


def pad(x, seconds):
    return np.concatenate([x, np.zeros(int(seconds * SR))])


# --- the sounds ----------------------------------------------------------------------------------

def recall_reach():
    """0-0.9 s the rip (crackling noise, tearing upward); 0.6-2.2 s a rising rush with a shimmer of bells."""
    total = 2.4
    buf = np.zeros(int(total * SR))
    # The tear: a burst of noise whose band sweeps up, peppered with crackles.
    n = int(0.9 * SR)
    tt = np.arange(n) / SR
    noise = RNG.standard_normal(n)
    rip = np.zeros(n)
    for lo in np.linspace(300, 2500, 12):
        seg = bandpass(noise, lo, lo * 1.8) * np.exp(-((tt - (lo / 2500) * 0.6) / 0.12) ** 2)
        rip += seg
    crackle = np.zeros(n)
    for _ in range(160):
        i = int(RNG.random() * n * 0.95)
        k = int(0.002 * SR)
        crackle[i:i + k] += RNG.standard_normal(k) * np.exp(-np.arange(k) / (0.0004 * SR)) * RNG.uniform(0.4, 1.4)
    crackle = highpass(crackle, 1500)
    place(buf, (rip * 0.7 + crackle * 0.5) * env(n, 0.02, 0.3), 0.0)
    # The rush: noise through a band sweeping upward, swelling; a sub drone under it.
    n = int(1.8 * SR)
    tt = np.arange(n) / SR
    rush = np.zeros(n)
    noise = RNG.standard_normal(n)
    chunks = 24
    for c in range(chunks):
        a, b = c * n // chunks, (c + 1) * n // chunks
        centre = 250 * (1 + c / chunks * 6)
        rush[a:b] = bandpass(noise, centre * 0.6, centre * 1.6)[a:b]
    swell = (tt / tt[-1]) ** 1.6
    drone = np.sin(2 * np.pi * 48 * tt) * 0.5 + np.sin(2 * np.pi * 72.5 * tt) * 0.25
    shimmer = sum(np.sin(2 * np.pi * f * tt + RNG.random() * 6) * (0.5 + 0.5 * np.sin(2 * np.pi * (3 + i) * tt))
                  for i, f in enumerate((1318.5, 1568.0, 1975.5, 2637.0))) * 0.12
    place(buf, (lowpass(rush, 5000) * 0.8 + drone * 0.6 + shimmer) * swell * env(n, 0.1, 0.15), 0.6)
    return reverb(buf, 2.2, 5000, 0.45)


def recall_take():
    """A deep grabbing thump at 0.15 s, then a thunderclap at 0.76 s that rolls away."""
    total = 3.0
    buf = np.zeros(int(total * SR))
    n = int(0.7 * SR)
    tt = np.arange(n) / SR
    f = 70 * (1 + 0.8 * np.exp(-tt / 0.04))
    thump = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.18)
    thump += lowpass(RNG.standard_normal(n), 600) * np.exp(-tt / 0.02) * 0.6
    place(buf, thump, 0.15, 1.0)
    # Rising suck of air into the clap.
    n = int(0.6 * SR)
    tt = np.arange(n) / SR
    suck = bandpass(RNG.standard_normal(n), 400, 3000) * (tt / tt[-1]) ** 3
    place(buf, suck * 0.5, 0.16)
    # The clap: a sharp crack, then a long low roll.
    n = int(2.2 * SR)
    tt = np.arange(n) / SR
    crack = highpass(RNG.standard_normal(n), 800) * np.exp(-tt / 0.03)
    roll = lowpass(RNG.standard_normal(n), 300, 3) * np.exp(-tt / 0.7) * (1 + 0.5 * np.sin(2 * np.pi * 3.1 * tt))
    place(buf, crack * 0.9 + roll * 2.2, 0.76)
    return reverb(buf, 2.6, 3000, 0.4)


def footstep(bright):
    n = int(0.25 * SR)
    tt = np.arange(n) / SR
    scuff = bandpass(RNG.standard_normal(n), 200, 900 + 2500 * bright) * np.exp(-tt / 0.035)
    heel = np.sin(2 * np.pi * (95 + 40 * bright) * tt) * np.exp(-tt / 0.03)
    return scuff * 0.8 + heel * 0.7


def stairs(down):
    total = 1.8
    buf = np.zeros(int(total * SR))
    steps = 5
    for k in range(steps):
        near = (steps - 1 - k) / (steps - 1) if down else k / (steps - 1)   # 1: close to you
        place(buf, footstep(0.3 + 0.6 * near), 0.08 + k * 0.3, 0.35 + 0.65 * near)
    wet = reverb(buf, 2.4 if down else 1.2, 2500 if down else 5000, 0.55 if down else 0.3)
    if not down:   # coming out into the open: a breath of wind
        tt = t_of(total)
        wind = bandpass(RNG.standard_normal(len(tt)), 300, 1500) * np.clip((tt - 0.9) / 0.9, 0, 1) ** 2 * 0.25
        wet = wet + wind
    return wet


def bell(fundamental, seconds):
    """A church bell: inharmonic partials (hum, prime, tierce, quint, nominal), each fading at its own rate."""
    tt = t_of(seconds)
    partials = ((0.5, 1.0, 3.5), (1.0, 0.8, 2.6), (1.183, 0.6, 2.0), (1.5, 0.35, 1.6), (2.0, 0.55, 1.4), (2.51, 0.25, 0.9), (3.0, 0.2, 0.7))
    out = np.zeros(len(tt))
    for ratio, amp, decay in partials:
        beat = 1 + 0.004 * np.sin(2 * np.pi * 1.3 * tt)
        out += amp * np.sin(2 * np.pi * fundamental * ratio * beat * tt) * np.exp(-tt / decay)
    strike = highpass(RNG.standard_normal(len(tt)), 2000) * np.exp(-tt / 0.01) * 0.3
    return out + strike


def death():
    b = bell(98.0, 3.6)
    return reverb(b, 3.0, 3500, 0.35)


def unique():
    """A swelling low brass chord (minor, with a flattened second on top) and a cymbal swell."""
    total = 2.4
    tt = t_of(total)
    chord = np.zeros(len(tt))
    for f, a in ((55.0, 1.0), (65.4, 0.7), (82.4, 0.6), (110.0, 0.5), (116.5, 0.35)):
        saw = signal.sawtooth(2 * np.pi * f * tt * (1 + 0.002 * RNG.standard_normal()))
        chord += a * saw
    swell = np.clip(tt / 0.9, 0, 1) ** 1.5 * np.exp(-np.clip(tt - 1.3, 0, None) / 0.5)
    brass = lowpass(chord, 1100, 2)
    cymbal = highpass(RNG.standard_normal(len(tt)), 5000) * np.clip(tt / 1.2, 0, 1) ** 3 * np.exp(-np.clip(tt - 1.2, 0, None) / 0.3) * 0.5
    return reverb(brass * swell * 0.5 + cymbal, 2.0, 4000, 0.4)


def danger():
    total = 2.4
    tt = t_of(total)
    rumble = lowpass(RNG.standard_normal(len(tt)), 120, 3) * 3
    drone = (np.sin(2 * np.pi * 61.7 * tt) + 0.6 * np.sin(2 * np.pi * 87.3 * tt) + 0.3 * np.sin(2 * np.pi * 92.5 * tt))
    wobble = 0.7 + 0.3 * np.sin(2 * np.pi * 0.9 * tt)
    shape = np.clip(tt / 0.6, 0, 1) * np.exp(-np.clip(tt - 1.6, 0, None) / 0.35)
    return reverb((rumble + drone * 0.5) * wobble * shape, 1.8, 2000, 0.4)


def fall():
    """A crack (0 s), a rush of air falling (0.1-1.3 s), and the landing thud (1.35 s)."""
    total = 2.0
    buf = np.zeros(int(total * SR))
    n = int(0.25 * SR)
    tt = np.arange(n) / SR
    place(buf, highpass(RNG.standard_normal(n), 900) * np.exp(-tt / 0.03) * 1.2
          + lowpass(RNG.standard_normal(n), 300) * np.exp(-tt / 0.08), 0.0)
    n = int(1.3 * SR)
    tt = np.arange(n) / SR
    noise = RNG.standard_normal(n)
    rush = np.zeros(n)
    for c in range(16):
        a, b = c * n // 16, (c + 1) * n // 16
        centre = 300 + 1800 * c / 16
        rush[a:b] = bandpass(noise, centre * 0.6, centre * 1.5)[a:b]
    place(buf, rush * (tt / tt[-1]) ** 1.2 * 0.9, 0.1)
    n = int(0.6 * SR)
    tt = np.arange(n) / SR
    f = 55 * (1 + 0.6 * np.exp(-tt / 0.03))
    thud = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.15) + lowpass(RNG.standard_normal(n), 500) * np.exp(-tt / 0.03)
    place(buf, thud * 1.4, 1.35)
    return reverb(buf, 1.2, 3000, 0.3)


def quest_complete():
    """A rising arpeggio of bell tones over a warm major chord."""
    total = 2.6
    buf = np.zeros(int(total * SR))
    for k, f in enumerate((523.25, 659.25, 783.99, 1046.5)):
        n = int(1.6 * SR)
        tt = np.arange(n) / SR
        tone = sum(a * np.sin(2 * np.pi * f * r * tt) * np.exp(-tt / d) for r, a, d in ((1, 1, 0.9), (2.0, 0.4, 0.5), (3.01, 0.2, 0.3)))
        place(buf, tone * 0.5, 0.05 + k * 0.14)
    tt = t_of(total)
    pad = sum(np.sin(2 * np.pi * f * tt) for f in (261.63, 329.63, 392.0)) * np.clip(tt / 0.5, 0, 1) * np.exp(-np.clip(tt - 1.2, 0, None) / 0.6)
    buf += lowpass(pad, 1500) * 0.25
    return reverb(buf, 2.0, 5000, 0.4)


def boss_slain():
    """A deep gong, then a low 'ah' swell (formant-filtered chords) that dies away."""
    total = 3.0
    tt = t_of(total)
    gong = sum(a * np.sin(2 * np.pi * 73.4 * r * tt * (1 + 0.003 * np.sin(2 * np.pi * 0.7 * tt))) * np.exp(-tt / d)
               for r, a, d in ((1, 1, 2.2), (1.52, 0.6, 1.6), (2.15, 0.5, 1.2), (2.9, 0.35, 0.8), (4.1, 0.25, 0.5)))
    gong += highpass(RNG.standard_normal(len(tt)), 1500) * np.exp(-tt / 0.02) * 0.3
    voices = sum(signal.sawtooth(2 * np.pi * f * tt * (1 + 0.004 * np.sin(2 * np.pi * (4.5 + i) * tt))) for i, f in enumerate((110.0, 164.8, 220.0)))
    choir = bandpass(voices, 500, 1100) * 0.6 + bandpass(voices, 700, 900)
    choir *= np.clip((tt - 0.3) / 0.8, 0, 1) * np.exp(-np.clip(tt - 1.4, 0, None) / 0.6) * 0.3
    return reverb(gong + choir, 2.6, 3500, 0.4)


SOUNDS = {
    "recall-reach": recall_reach, "recall-take": recall_take,
    "stairs-down": lambda: stairs(True), "stairs-up": lambda: stairs(False),
    "death": death, "unique": unique, "danger": danger,
    "fall": fall, "quest-complete": quest_complete, "boss-slain": boss_slain,
}


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for name, make in SOUNDS.items():
        audio = make()
        audio = audio - audio.mean()
        # About -20 dBFS mean, like the bundled effects, and no peak over -1 dBFS.
        audio *= 10 ** (-20 / 20) / np.sqrt(np.mean(audio ** 2))
        peak = np.max(np.abs(audio))
        if peak > 10 ** (-1 / 20):
            audio *= 10 ** (-1 / 20) / peak
        # Fade the tail to silence.
        k = int(0.05 * SR)
        audio[-k:] *= np.linspace(1, 0, k)
        stereo = np.stack([audio, audio], axis=1).astype(np.float32)
        path = os.path.join(out, name + ".ogg")
        with sf.SoundFile(path, "w", SR, 2, format="OGG", subtype="VORBIS") as f:
            for i in range(0, len(stereo), 16384):
                f.write(stereo[i:i + 16384])
        print(f"{path}: {len(audio) / SR:.1f} s")


if __name__ == "__main__":
    main()
