# Builds the intro textures. Channels: R = glyph, G = soft glow halo, B = reveal order (0 first .. 1 last).
import sys, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

logo_src, out_dir = sys.argv[1], sys.argv[2]
rng = np.random.default_rng(7)

def blur(a, r):
    return np.asarray(Image.fromarray((a * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), dtype=np.float32) / 255

def noise(h, w, scale):
    small = rng.random((max(2, h // scale), max(2, w // scale))).astype(np.float32)
    return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), dtype=np.float32) / 255

def save(glyph, halo, order, path):
    rgb = np.stack([glyph, halo, order], -1)
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB").save(path)

# ---- Logo: the dark strokes of the wheel plus the disc's silhouette become the glowing glyph.
S = 1024
pad = 112
src = Image.open(logo_src).convert("RGBA").resize((S - 2 * pad, S - 2 * pad), Image.LANCZOS)
canvas = Image.new("RGBA", (S, S), (0, 0, 0, 0)); canvas.paste(src, (pad, pad))
a = np.asarray(canvas, dtype=np.float32) / 255
alpha, lum = a[..., 3], a[..., :3].mean(-1)
strokes = np.clip((0.45 - lum) / 0.2, 0, 1) * (alpha > 0.5)
disc = (alpha > 0.5).astype(np.float32)
rim = np.clip(blur(disc, 6) * 2 - 1, 0, 1) - np.clip(blur(disc, 6) * 2 - 1.9, 0, 1) * 10
rim = np.clip((blur(disc, 3) > 0.15) & (blur(disc, 3) < 0.85), 0, 1).astype(np.float32)
rim = blur(rim, 1.2)
glyph = np.clip(np.maximum(strokes, rim * 1.4), 0, 1)
glyph = blur(glyph, 0.8)
halo = np.clip(blur(glyph, 10) * 1.6 + blur(glyph, 30) * 1.2, 0, 1)
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
ang = (np.arctan2(yy - S / 2, xx - S / 2) / (2 * math.pi) + 0.25) % 1.0   # sweeps from the top, clockwise
rad = np.hypot(yy - S / 2, xx - S / 2) / (S / 2)
order = np.clip(0.55 * (1 - rad) + 0.35 * ang + 0.1 * noise(S, S, 24), 0, 1)
save(glyph, halo, order, f"{out_dir}/IntroLogo.png")

# ---- Wordmark "DEADLOCK": big first D and last K, worn edges like the game's title.
W, H = 2048, 512
img = Image.new("L", (W, H), 0); d = ImageDraw.Draw(img)
big = ImageFont.truetype("C:/Windows/Fonts/georgiab.ttf", 300)
small = ImageFont.truetype("C:/Windows/Fonts/georgiab.ttf", 230)
letters = [("D", big)] + [(c, small) for c in "EADLOC"] + [("K", big)]
gap = 18
widths = [d.textbbox((0, 0), c, font=f)[2] - d.textbbox((0, 0), c, font=f)[0] for c, f in letters]
x = (W - (sum(widths) + gap * (len(letters) - 1))) / 2
base = 360
for (c, f), w in zip(letters, widths):
    bb = d.textbbox((0, 0), c, font=f, anchor="ls")
    d.text((x - bb[0], base), c, font=f, fill=255, anchor="ls")
    x += w + gap
m = np.asarray(img, dtype=np.float32) / 255
# Distress: chew the edges with noise and knock a few specks out of the strokes.
edge = np.clip(blur(m, 3) * (1 - blur(m, 3)) * 4, 0, 1)
m = np.clip(m - edge * (noise(H, W, 6) > 0.62) - (noise(H, W, 3) > 0.96) * 0.9 * m, 0, 1)
glyph = blur(np.clip((m - 0.35) * 3, 0, 1), 0.7)
halo = np.clip(blur(glyph, 8) * 1.4 + blur(glyph, 26) * 1.1, 0, 1)
xx = np.tile(np.linspace(0, 1, W, dtype=np.float32), (H, 1))
order = np.clip(0.8 * xx + 0.2 * noise(H, W, 16), 0, 1)
save(glyph, halo, order, f"{out_dir}/IntroWordmark.png")

# ---- Facade: night windows on a dark wall (emission map), tiles every 14 m.
F = 512
fac = np.zeros((F, F), np.float32)
cols, rows = 6, 6
for r in range(rows):
    for c in range(cols):
        if rng.random() < 0.3:
            x0 = int(c * F / cols + F / cols * 0.25); x1 = int((c + 1) * F / cols - F / cols * 0.25)
            y0 = int(r * F / rows + F / rows * 0.3); y1 = int((r + 1) * F / rows - F / rows * 0.15)
            fac[y0:y1, x0:x1] = 0.55 + 0.45 * rng.random()
fac = blur(fac, 1)
warm = np.stack([fac * 1.0, fac * 0.72, fac * 0.38], -1)
Image.fromarray((np.clip(warm, 0, 1) * 255).astype(np.uint8), "RGB").save(f"{out_dir}/IntroFacadeLights.png")
print("ok")
