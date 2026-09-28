"""Generate the MiniClip icons from the approved letter-M design.

Source geometry: design/icon-concepts/02-letter-m.svg (128 by 128 units).
Run from the repository root: python tools/make-icons.py
"""

from pathlib import Path

from PIL import Image, ImageDraw


BACKGROUND = (0x20, 0x20, 0x20, 0xFF)
FOREGROUND = (0xFF, 0xFF, 0xFF, 0xFF)
SUPERSAMPLE = 8


def draw_mark(size: int) -> Image.Image:
    """Render the rounded tile and M at the requested size."""
    pixels = size * SUPERSAMPLE
    image = Image.new("RGBA", (pixels, pixels), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    scale = pixels / 128

    draw.rounded_rectangle(
        (0, 0, pixels - 1, pixels - 1),
        radius=round(27 * scale),
        fill=BACKGROUND,
    )

    points = [(34, 94), (34, 35), (64, 69), (94, 35), (94, 94)]
    scaled = [(round(x * scale), round(y * scale)) for x, y in points]
    stroke = round(10 * scale)
    draw.line(scaled, fill=FOREGROUND, width=stroke, joint="curve")

    # Pillow does not apply SVG-style round caps to polyline endpoints.
    radius = stroke / 2
    for x, y in scaled:
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=FOREGROUND)

    return image.resize((size, size), Image.Resampling.LANCZOS)


def write_ico(path: Path, sizes: list[int]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    # Pillow creates ICO subimages from the base image. The base must be at least
    # as large as the largest requested size; saving a 16 px base silently drops
    # every larger frame.
    base = draw_mark(max(sizes))
    base.save(path, format="ICO", sizes=[(size, size) for size in sizes])
    print(f"wrote {path} ({', '.join(str(size) for size in sizes)})")


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    assets = root / "src" / "MiniClip" / "Assets"

    write_ico(assets / "miniclip.ico", [16, 20, 24, 32, 48, 64, 128, 256])
    write_ico(assets / "miniclip-tray.ico", [16, 20, 24, 32, 48])

    shot = root / "design" / "shots"
    shot.mkdir(parents=True, exist_ok=True)
    draw_mark(256).save(shot / "icon-256.png")

    sizes = (16, 24, 32, 64, 128)
    padding = 16
    preview = Image.new(
        "RGBA",
        (sum(sizes) + padding * (len(sizes) + 1), 128 + padding * 2),
        (0xEC, 0xEC, 0xEC, 0xFF),
    )
    x = padding
    for size in sizes:
        preview.alpha_composite(draw_mark(size), (x, padding))
        x += size + padding
    preview.save(shot / "icon-sizes.png")
    print(f"wrote {shot / 'icon-256.png'} and {shot / 'icon-sizes.png'}")


if __name__ == "__main__":
    main()
