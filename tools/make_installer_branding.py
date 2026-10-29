#!/usr/bin/env python3
"""Generate the Gantt-It MSI setup branding (installer/branding/).

Follows rst-c's installer artwork (rst-c/installer/branding/): a Designs/OS
navy sidebar with a white wordmark and underline on the welcome/exit dialog,
and a white banner with the product mark in the right corner over a navy rule.
The mark is the ribbon-icon glyph from tools/make_icons.py: three staggered
#4F8EF7 bars on a #D9D9D9 rounded tile.

Usage:  python3 tools/make_installer_branding.py
Writes: installer/branding/dialog.bmp   493x312, 24-bit BMP (WixUIDialogBmp)
        installer/branding/banner.bmp   493x58,  24-bit BMP (WixUIBannerBmp)
        installer/branding/ganttit.ico  16/32/48/256 px      (ARPPRODUCTICON)
Needs:  Pillow, and a bold sans font (Open Sans / DejaVu Sans / Liberation Sans)
        for the wordmark. The outputs are committed; rerun only to change them.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from make_icons import GLYPH, SPECS, SUPERSAMPLE, TILE, render

NAVY = (0x3B, 0x59, 0x98)  # rst-c installer sidebar / rule colour
WHITE = (0xFF, 0xFF, 0xFF)

# Glyph bars on the 32 px grid (make_icons.SPECS[32]); scaled for larger marks.
RADIUS_32, BARS_32 = SPECS[32]

FONTS = [
    "/usr/share/fonts/TTF/OpenSans-Bold.ttf",
    "/usr/share/fonts/truetype/open-sans/OpenSans-Bold.ttf",
    "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/liberation/LiberationSans-Bold.ttf",
    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
]

OUT = Path(__file__).resolve().parent.parent / "installer" / "branding"


def mark(size: int) -> Image.Image:
    """The icon tile at any size. 16 and 32 px are the exact ribbon icons."""
    if size in SPECS:
        radius, bars = SPECS[size]
        return render(size, radius, bars)
    k = size / 32
    s = SUPERSAMPLE
    big = Image.new("RGBA", (size * s, size * s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    draw.rounded_rectangle((0, 0, size * s - 1, size * s - 1), radius=RADIUS_32 * k * s, fill=TILE)
    for x0, y0, x1, y1 in BARS_32:
        draw.rectangle(
            (round(x0 * k * s), round(y0 * k * s), round(x1 * k * s) - 1, round(y1 * k * s) - 1),
            fill=GLYPH,
        )
    return big.reduce(s)


def font(px: int) -> ImageFont.FreeTypeFont:
    for path in FONTS:
        if Path(path).exists():
            return ImageFont.truetype(path, px)
    raise SystemExit("no bold sans font found; edit FONTS in make_installer_branding.py")


def dialog() -> Image.Image:
    img = Image.new("RGB", (493, 312), WHITE)
    draw = ImageDraw.Draw(img)
    side = 165  # same sidebar width as rst-c; WixUI draws its text right of it
    draw.rectangle((0, 0, side - 1, 311), fill=NAVY)

    tile = mark(64)
    img.paste(tile, ((side - 64) // 2, 72), tile)

    f = font(26)
    text = "Gantt-It"
    x0, y0, x1, y1 = draw.textbbox((0, 0), text, font=f)
    tx = (side - (x1 - x0)) // 2 - x0
    draw.text((tx, 160 - y0), text, font=f, fill=WHITE)

    draw.rectangle((20, 205, side - 21, 206), fill=WHITE)
    return img


def banner() -> Image.Image:
    img = Image.new("RGB", (493, 58), WHITE)
    draw = ImageDraw.Draw(img)
    tile = mark(32)
    img.paste(tile, (493 - 32 - 16, (56 - 32) // 2), tile)
    draw.rectangle((0, 56, 492, 57), fill=NAVY)
    return img


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    dialog().save(OUT / "dialog.bmp")
    banner().save(OUT / "banner.bmp")
    sizes = [16, 32, 48, 256]
    icons = [mark(n) for n in sizes]
    icons[-1].save(OUT / "ganttit.ico", sizes=[(n, n) for n in sizes], append_images=icons[:-1])
    for name in ("dialog.bmp", "banner.bmp", "ganttit.ico"):
        print(f"wrote installer/branding/{name}")


if __name__ == "__main__":
    main()
