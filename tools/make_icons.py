#!/usr/bin/env python3
"""Generate the Gantt-It ribbon icons (spec §8.2).

Matches rst-c's icon style (rst-c/src/RST.Engine/Assets/icons/): a full-bleed
light-grey #D9D9D9 tile with anti-aliased rounded (transparent) corners and a
flat single-colour glyph in rst-c blue #4F8EF7. No outlines, gradients or text.
The glyph is three horizontal bars staggered left to right like a Gantt
schedule, drawn on whole pixels so it stays crisp.

Revit's dark UI theme (spec §2.5) gets a dark variant of each icon: the same
glyph in a lighter blue on a dark tile, so the button doesn't sit as a bright
square on the dark ribbon. The add-in swaps them when Revit's theme changes.

Usage:  python3 tools/make_icons.py
Writes: src/GanttIt.Revit/Resources/icons/gantt_32.png, gantt_16.png,
        gantt_32_dark.png and gantt_16_dark.png
Needs:  Pillow
"""

from pathlib import Path

from PIL import Image, ImageDraw

# variant suffix -> (tile, glyph)
VARIANTS = {
    "": ((0xD9, 0xD9, 0xD9, 0xFF), (0x4F, 0x8E, 0xF7, 0xFF)),
    # Glyph on tile 5.2:1; the tile is a shade darker than Revit's dark ribbon.
    "_dark": ((0x2A, 0x30, 0x3A, 0xFF), (0x6F, 0xA3, 0xFA, 0xFF)),
}
SUPERSAMPLE = 8

# size -> (corner radius px, bars as (x0, y0, x1, y1) in px, end-exclusive)
SPECS = {
    32: (3, [(5, 5, 17, 11), (11, 13, 23, 19), (17, 21, 27, 27)]),
    16: (1.5, [(3, 3, 9, 5), (6, 7, 12, 9), (9, 11, 13, 13)]),
}

OUT = Path(__file__).resolve().parent.parent / "src" / "GanttIt.Revit" / "Resources" / "icons"


def render(
    size: int,
    radius: float,
    bars: list[tuple[int, int, int, int]],
    tile: tuple[int, int, int, int],
    glyph: tuple[int, int, int, int],
) -> Image.Image:
    s = SUPERSAMPLE
    big = Image.new("RGBA", (size * s, size * s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    draw.rounded_rectangle((0, 0, size * s - 1, size * s - 1), radius=radius * s, fill=tile)
    for x0, y0, x1, y1 in bars:
        draw.rectangle((x0 * s, y0 * s, x1 * s - 1, y1 * s - 1), fill=glyph)
    # Box-filter downsampling: whole-pixel bars stay exact, corners anti-alias.
    return big.reduce(s)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for suffix, (tile, glyph) in VARIANTS.items():
        for size, (radius, bars) in SPECS.items():
            path = OUT / f"gantt_{size}{suffix}.png"
            render(size, radius, bars, tile, glyph).save(path, optimize=True)
            print(f"wrote {path.relative_to(OUT.parents[3])}")


if __name__ == "__main__":
    main()
