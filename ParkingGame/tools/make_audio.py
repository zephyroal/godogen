"""Procedurally synthesizes the two success jingles (16-bit mono WAV, 44.1 kHz).

No dependencies — Python stdlib only. Run from anywhere:
    python tools/make_audio.py

Outputs:
    assets/audio/success.wav     — normal park: warm C-major bell arpeggio + pad
    assets/audio/success_suv.wav — SUV park: quicker, brassier fanfare + shimmer
"""
import math
import os
import struct
import wave

SR = 44100
DUR = 3.2

BELL = ((1, 1.0), (2, 0.42), (3, 0.16), (4, 0.06))              # soft chime
BRASS = ((1, 1.0), (2, 0.55), (3, 0.32), (4, 0.18), (5, 0.08))  # brighter fanfare

C4, E4, G4 = 261.63, 329.63, 392.00
C5, E5, G5 = 523.25, 659.26, 783.99
C6, E6, G6 = 1046.50, 1318.50, 1568.00


def synth(events, harmonics, decay, amp=0.5):
    buf = [0.0] * int(DUR * SR)
    for (t0, freq, a) in events:
        n0 = int(t0 * SR)
        n1 = min(n0 + int(1.7 * SR), len(buf))
        for n in range(n0, n1):
            t = (n - n0) / SR
            e = math.exp(-decay * t) * min(1.0, t / 0.006)  # attack + exp decay
            s = sum(w * math.sin(2.0 * math.pi * freq * k * t) for (k, w) in harmonics)
            buf[n] += a * e * s
    peak = max(abs(v) for v in buf) or 1.0
    return [v / peak * 0.82 for v in buf]


def write(path, buf):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in buf))


def main():
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "audio")

    normal = synth(
        [(0.00, C5, 1.0), (0.14, E5, 1.0), (0.28, G5, 1.0), (0.42, C6, 1.0),
         (0.58, C4, 0.5), (0.58, E4, 0.5), (0.58, G4, 0.5)],
        BELL, decay=3.2)
    suv = synth(
        [(0.00, G4, 1.0), (0.11, C5, 1.0), (0.22, E5, 1.0), (0.33, G5, 1.0),
         (0.44, C6, 1.1), (0.60, E6, 0.8), (0.72, G6, 0.8)],
        BRASS, decay=2.6)

    write(os.path.join(root, "success.wav"), normal)
    write(os.path.join(root, "success_suv.wav"), suv)
    print("wrote", os.path.normpath(os.path.join(root, "success.wav")),
          "and success_suv.wav")


if __name__ == "__main__":
    main()
