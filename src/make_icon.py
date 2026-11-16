import os

from PIL import Image, ImageDraw

S = 1024
BACKGROUND = (15, 23, 42, 255)
BELL = (255, 183, 0, 255)


def bell(draw, x, y, w, h):
    draw.pieslice((x + w * 0.22, y + h * 0.12, x + w * 0.78, y + h * 0.68), 180, 360, fill=BELL)
    draw.polygon([
        (x + w * 0.22, y + h * 0.40), (x + w * 0.78, y + h * 0.40), (x + w * 0.84, y + h * 0.66),
        (x + w * 0.96, y + h * 0.78), (x + w * 0.04, y + h * 0.78), (x + w * 0.16, y + h * 0.66),
    ], fill=BELL)
    draw.ellipse((x + w * 0.38, y + h * 0.76, x + w * 0.62, y + h * 0.96), fill=BELL)
    draw.ellipse((x + w * 0.43, y + h * 0.02, x + w * 0.57, y + h * 0.16), fill=BELL)


base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(base)
d.rounded_rectangle((32, 32, S - 32, S - 32), radius=220, fill=BACKGROUND)
bell(d, 192, 170, 640, 680)
base.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
