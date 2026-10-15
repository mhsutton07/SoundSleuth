"""Check a SoundSleuth clip: parses as WAV, is not silent, and is at most 20 seconds long."""
import array
import sys
import wave


def main(path):
    with wave.open(path, "rb") as w:
        ch, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)
    if width != 2:
        print("FAIL: expected 16-bit samples, got %d-bit" % (width * 8))
        return 1
    samples = array.array("h")
    samples.frombytes(raw)
    peak = max((abs(s) for s in samples), default=0) / 32767.0
    seconds = frames / float(rate)
    print("%s: %d ch, %d Hz, %.2f s, peak %.3f" % (path, ch, rate, seconds, peak))
    if peak < 0.01:
        print("FAIL: clip is silent (peak %.4f < 0.01)" % peak)
        return 1
    if seconds > 20.0:
        print("FAIL: clip is longer than 20 s")
        return 1
    print("OK")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("usage: check_wav.py <file.wav>")
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
