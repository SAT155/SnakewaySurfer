"""Draws thunderstore/SnakewaySurfer/icon.png (256x256): a three-lane track running into a
sunset, with a coin trail. Run: python3 tools/make_icon.py"""
import math
from pathlib import Path
from PIL import Image, ImageDraw

S = 256
img = Image.new("RGB", (S, S))
d = ImageDraw.Draw(img)
# Sky gradient
for y in range(S):
    t = y / S
    d.line([(0, y), (S, y)], fill=(int(40 + 200 * t), int(60 + 90 * t), int(120 - 40 * t)))
horizon = 110
d.ellipse([88, horizon - 42, 168, horizon + 38], fill=(255, 200, 90))
# Ground
d.polygon([(0, horizon), (S, horizon), (S, S), (0, S)], fill=(52, 82, 38))
# Track (trapezoid) and lane lines
cx = S / 2
top_w, bot_w = 24, 230
d.polygon([(cx - top_w / 2, horizon), (cx + top_w / 2, horizon), (cx + bot_w / 2, S), (cx - bot_w / 2, S)], fill=(120, 112, 100))
for f in (-1 / 6, 1 / 6):
    d.line([(cx + f * top_w, horizon), (cx + f * bot_w, S)], fill=(230, 220, 190), width=3)
# Trees on the sides
for x, h in [(20, 70), (52, 50), (204, 60), (236, 80), (76, 34), (182, 36)]:
    base = horizon + h * 0.35
    d.polygon([(x, base - h), (x - h * 0.28, base), (x + h * 0.28, base)], fill=(26, 60, 30))
# Coin trail in the middle lane
for i in range(5):
    t = (i + 1) / 6
    y = horizon + (S - horizon) * t ** 1.3
    r = 3 + 12 * t
    d.ellipse([cx - r, y - r * 0.8, cx + r, y + r * 0.8], fill=(250, 210, 60), outline=(150, 110, 20), width=2)
out = Path(__file__).resolve().parent.parent / "thunderstore" / "SnakewaySurfer" / "icon.png"
img.save(out)
print("wrote", out)
