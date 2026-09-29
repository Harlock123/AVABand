#!/usr/bin/env python3
"""Synthesise AVABand's ambience loops for the special levels (CC0, made for AVABand).

The bundled ambience (src/Angband.Avalonia/ambience) had no loop for caverns, fortresses (a hard
centre), moria levels, lairs or gauntlets; these are made here, all from noise and oscillators — no
samples — and seamless: each is rendered long and its tail cross-faded into its head.

  ambient-cavern    water dripping into pools, echoing in stone, over a low draught
  ambient-fortress  wind in great halls, a far forge-hammer, now and then a chain
  ambient-moria     deep halls, slow drips, and far off — now and then — drums in the deep
  ambient-lair      slow heavy breathing in the dark, and a low heartbeat
  ambient-gauntlet  rushing, gusting wind with a thin metallic whine

Needs numpy, scipy and soundfile. Usage: ambience_loops.py <out folder> [--seconds 60]
"""
import argparse
import os

import numpy as np
import soundfile as sf
from scipy import signal

SR = 44100
RNG = np.random.default_rng(4242)


def lowpass(x, cutoff, order=2):
    b, a = signal.butter(order, min(cutoff, SR / 2 - 100) / (SR / 2), "low")
    return signal.lfilter(b, a, x)


def bandpass(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], "band")
    return signal.lfilter(b, a, x)


def slow(n, period_s, depth=1.0):
    """A slowly wandering 0..1 control signal."""
    points = int(n / (period_s * SR)) + 3
    ctrl = RNG.random(points)
    x = np.interp(np.arange(n), np.linspace(0, n, points), ctrl)
    return 1 - depth + depth * lowpass(x, 1.0 / period_s, 1).clip(0, 1)


def reverb(x, seconds, damp=4000, wet=0.5):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = RNG.standard_normal(n) * np.exp(-t / (seconds / 5))
    ir = lowpass(ir, damp)
    ir[: int(0.015 * SR)] = 0
    w = signal.fftconvolve(x, ir)[: len(x)]
    w *= np.sqrt(np.mean(x ** 2) / max(np.mean(w ** 2), 1e-12))
    return x * (1 - wet) + w * wet


def drip(freq):
    """A drop falling into a pool: a short sine that bends upward, quickly damped."""
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    f = freq * (1 + 1.8 * t / 0.12)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.025)


def thud(freq, length=0.6, click=0.3):
    n = int(length * SR)
    t = np.arange(n) / SR
    f = freq * (1 + 0.5 * np.exp(-t / 0.03))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / (length / 4))
    x += click * lowpass(RNG.standard_normal(n), 900) * np.exp(-t / 0.01)
    return x


def clank(freq):
    n = int(1.2 * SR)
    t = np.arange(n) / SR
    x = sum(a * np.sin(2 * np.pi * freq * r * t) * np.exp(-t / d)
            for r, a, d in ((1, 1, 0.5), (2.76, 0.6, 0.3), (5.4, 0.4, 0.15), (8.9, 0.25, 0.08)))
    return x + 0.4 * RNG.standard_normal(n) * np.exp(-t / 0.004)


def place(buf, sound, at, gain=1.0, pan=0.0):
    i = int(at * SR) % len(buf[0])
    for ch, g in ((0, np.sqrt(0.5 * (1 - pan))), (1, np.sqrt(0.5 * (1 + pan)))):
        end = min(len(buf[ch]), i + len(sound))
        buf[ch][i:end] += sound[: end - i] * gain * g
        rest = len(sound) - (end - i)
        if rest > 0:  # wrap round: the loop's end runs into its start
            buf[ch][:rest] += sound[end - i:] * gain * g


def air(n, lo, hi, gust_period, gust_depth):
    x = bandpass(RNG.standard_normal(n), lo, hi)
    return x * slow(n, gust_period, gust_depth)


def stereo_noise(n, lo, hi, period, depth):
    return [air(n, lo, hi, period, depth), air(n, lo, hi, period * 1.3, depth)]


def cavern(n, secs):
    buf = stereo_noise(n, 60, 380, 7, 0.6)
    buf = [b * 0.5 for b in buf]
    for _ in range(int(secs * 1.1)):
        place(buf, drip(RNG.uniform(900, 2400)), RNG.uniform(0, secs), RNG.uniform(0.15, 0.5), RNG.uniform(-0.9, 0.9))
    return [reverb(b, 2.8, 3500, 0.6) for b in buf]


def fortress(n, secs):
    buf = stereo_noise(n, 90, 900, 9, 0.8)
    buf = [b * 0.6 for b in buf]
    t = RNG.uniform(1, 4)
    while t < secs:  # a far forge hammer, in fours
        for k in range(4):
            place(buf, clank(RNG.uniform(420, 520)), t + k * 0.9, 0.12, -0.5)
        t += RNG.uniform(9, 16)
    for _ in range(int(secs / 20)):  # a chain, now and then
        at = RNG.uniform(0, secs)
        for k in range(6):
            place(buf, clank(RNG.uniform(1800, 2600)), at + k * 0.07 + RNG.uniform(0, 0.03), 0.05, 0.6)
    return [reverb(b, 3.5, 2500, 0.7) for b in buf]


def moria(n, secs):
    buf = stereo_noise(n, 40, 250, 12, 0.5)
    buf = [b * 0.6 for b in buf]
    for _ in range(int(secs * 0.4)):
        place(buf, drip(RNG.uniform(500, 1100)), RNG.uniform(0, secs), RNG.uniform(0.1, 0.3), RNG.uniform(-0.8, 0.8))
    t = RNG.uniform(8, 14)
    while t < secs - 4:  # drums in the deep: doom, boom, doom
        for k, g in ((0, 0.5), (0.55, 0.35), (1.1, 0.5), (2.2, 0.45), (2.75, 0.3), (3.3, 0.45)):
            place(buf, thud(55, 1.2, 0.2), t + k, g, -0.2)
        t += RNG.uniform(18, 28)
    return [reverb(b, 4.5, 1800, 0.75) for b in buf]


def lair(n, secs):
    base = stereo_noise(n, 50, 300, 10, 0.5)
    buf = [b * 0.35 for b in base]
    t = 0.5
    while t < secs:  # slow breathing: in, out
        for length, lo, hi, g in ((1.6, 120, 700, 0.55), (2.0, 80, 450, 0.7)):
            k = int(length * SR)
            env = np.sin(np.linspace(0, np.pi, k)) ** 2
            place(buf, bandpass(RNG.standard_normal(k), lo, hi) * env, t, g, 0.1)
            t += length
        t += RNG.uniform(0.8, 1.6)
    t = 0.3
    while t < secs:  # a heartbeat, lub-dub
        place(buf, thud(38, 0.4, 0.05), t, 0.35)
        place(buf, thud(34, 0.35, 0.05), t + 0.28, 0.25)
        t += 1.45
    return [reverb(b, 1.8, 1500, 0.4) for b in buf]


def gauntlet(n, secs):
    buf = stereo_noise(n, 140, 1300, 3.5, 0.9)
    k = np.arange(n) / SR
    whine = np.sin(2 * np.pi * (1480 + 60 * np.sin(2 * np.pi * k / 7.3)) * k) * slow(n, 5, 0.9)
    return [lowpass(reverb(b * 0.8 + 0.03 * whine, 1.5, 3000, 0.3), 1800) for b in buf]


LOOPS = {"ambient-cavern": cavern, "ambient-fortress": fortress, "ambient-moria": moria, "ambient-lair": lair, "ambient-gauntlet": gauntlet}


def seamless(buf, fade):
    """Folds the extra tail back over the head with an equal-power cross-fade, so the end runs into the start."""
    out = []
    for ch in buf:
        body, tail = ch[:-fade].copy(), ch[-fade:]
        w = np.linspace(0, np.pi / 2, fade)
        body[:fade] = body[:fade] * np.sin(w) + tail * np.cos(w)
        out.append(body)
    return np.stack(out, axis=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--seconds", type=float, default=60)
    a = ap.parse_args()
    fade = int(4 * SR)
    for name, make in LOOPS.items():
        n = int(a.seconds * SR) + fade
        audio = seamless(make(n, a.seconds + 4), fade)
        audio -= audio.mean(axis=0)
        # About -28 dBFS mean, like the other loops, and no peak over -3 dBFS.
        audio *= 10 ** (-28 / 20) / np.sqrt(np.mean(audio ** 2))
        peak = np.max(np.abs(audio))
        if peak > 10 ** (-3 / 20):
            audio *= 10 ** (-3 / 20) / peak
        path = os.path.join(a.out, name + ".ogg")
        audio = audio.astype(np.float32)
        with sf.SoundFile(path, "w", SR, 2, format="OGG", subtype="VORBIS") as f:
            for i in range(0, len(audio), 16384):
                f.write(audio[i:i + 16384])
        print(f"{path}: {len(audio) / SR:.0f} s, rms {20 * np.log10(np.sqrt(np.mean(audio ** 2))):.1f} dB")


if __name__ == "__main__":
    main()
