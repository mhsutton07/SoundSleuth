"""Draw the SoundSleuth icon: a magnifying glass over sound bars.

Writes assets/SoundSleuth.ico (16-256 px) and assets/icon-256.png.
Each size is rendered at 8x and downsampled; sizes <= 24 px use a simplified mark
(fewer, thicker bars, heavier ring) so the shape survives at taskbar/tray size.
Requires Pillow.  Run: python assets/make_icon.py
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 8  # supersampling factor

BG_TOP = (36, 52, 96)
BG_BOTTOM = (20, 28, 56)
RING = (240, 244, 250)
HANDLE = (245, 184, 61)
BARS = (61, 219, 196)
LENS_FILL = (14, 20, 42)


def render(size):
    n = size * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))

    # Rounded-square tile with a vertical gradient.
    grad = Image.new("RGBA", (n, n))
    gd = ImageDraw.Draw(grad)
    for y in range(n):
        t = y / (n - 1)
        c = tuple(int(BG_TOP[i] + (BG_BOTTOM[i] - BG_TOP[i]) * t) for i in range(3))
        gd.line([(0, y), (n, y)], fill=c + (255,))
    mask = Image.new("L", (n, n), 0)
    inset = int(n * 0.03)
    ImageDraw.Draw(mask).rounded_rectangle([inset, inset, n - inset, n - inset], radius=int(n * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    small = size <= 24

    # Lens geometry (upper-left), handle runs to the lower-right.
    cx, cy = n * 0.43, n * 0.43
    r = n * (0.27 if small else 0.255)
    ring_w = n * (0.10 if small else 0.065)

    # Handle first so the ring overlaps its root.
    hx0, hy0 = cx + r * 0.62, cy + r * 0.62
    hx1, hy1 = n * 0.80, n * 0.80
    hw = n * (0.13 if small else 0.10)
    d.line([(hx0, hy0), (hx1, hy1)], fill=HANDLE, width=int(hw))
    d.ellipse([hx1 - hw / 2, hy1 - hw / 2, hx1 + hw / 2, hy1 + hw / 2], fill=HANDLE)

    # Lens interior and ring.
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=LENS_FILL)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=RING, width=int(ring_w))

    # Equalizer bars inside the lens.
    heights = [0.45, 0.95, 0.65] if small else [0.35, 0.75, 1.0, 0.6, 0.3]
    inner = r - ring_w * 0.9
    count = len(heights)
    span = inner * 1.35
    bar_w = span / (count * 1.6)
    gap = (span - bar_w * count) / (count - 1)
    x = cx - span / 2
    for h in heights:
        bh = inner * 1.3 * h
        d.rounded_rectangle([x, cy - bh / 2, x + bar_w, cy + bh / 2], radius=bar_w / 2, fill=BARS)
        x += bar_w + gap

    return img.resize((size, size), Image.LANCZOS)


def main():
    frames = [render(s) for s in SIZES]
    big = frames[-1]
    big.save(os.path.join(HERE, "icon-256.png"))
    big.save(os.path.join(HERE, "SoundSleuth.ico"), format="ICO",
             sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
    # Preview sheet for checking legibility at every size.
    sheet = Image.new("RGBA", (sum(SIZES) + 10 * len(SIZES) + 10, 276), (240, 240, 240, 255))
    x = 10
    for f in frames:
        sheet.paste(f, (x, 266 - f.size[1]), f)
        x += f.size[0] + 10
    sheet.save(os.path.join(HERE, "icon-preview.png"))
    print("wrote", os.path.join(HERE, "SoundSleuth.ico"))


if __name__ == "__main__":
    main()
