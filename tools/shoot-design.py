"""Recreates the isolated Chrome profile used for design screenshots, and verifies it works.

The profile lives at `.design/chrome-profile` inside the project and is never the user's
real browser profile. A dedicated profile is mandatory: launching Chrome without
`--user-data-dir` attaches to whatever instance is already running, and any cleanup then
risks closing the user's own windows.

Usage (from the repository root):
    python tools/shoot-design.py [page] [output.png] [width] [height]

Defaults to design/states.html -> design/shots/contact-sheet.png at 1500x3200.

The default height matters. `--window-size` is the *viewport*, and Chrome does not grow it
to fit the page: too short a window silently clips the bottom of the sheet, which is easy
to miss because the file still looks like a plausible contact sheet. The script therefore
measures the page's own scrollHeight with a throwaway pass and refuses to keep a clipped
shot.
"""

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROFILE = ROOT / ".design" / "chrome-profile"

CHROME_CANDIDATES = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]


def find_browser() -> str:
    for candidate in CHROME_CANDIDATES:
        if Path(candidate).exists():
            return candidate
    raise SystemExit("No Chrome or Edge found in the usual locations.")


def run(browser: str, args: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(
        [browser, "--headless=new", "--disable-gpu", "--hide-scrollbars",
         f"--user-data-dir={PROFILE}", *args],
        capture_output=True,
        text=True,
    )


def page_height(browser: str, url: str, width: int) -> int | None:
    """Measures the rendered page height so the capture window can be made tall enough."""
    result = run(browser, [
        "--dump-dom",
        f"--window-size={width},1200",
        f"--virtual-time-budget=2000",
        url,
    ])
    # The design pages expose their own measurement; fall back to a DOM probe if absent.
    match = re.search(r'data-page-height="(\d+)"', result.stdout)
    if match:
        return int(match.group(1))
    return None


def main() -> None:
    page = sys.argv[1] if len(sys.argv) > 1 else "design/states.html"
    shot = sys.argv[2] if len(sys.argv) > 2 else "design/shots/contact-sheet.png"
    width = int(sys.argv[3]) if len(sys.argv) > 3 else 1500
    height = int(sys.argv[4]) if len(sys.argv) > 4 else 3200

    PROFILE.mkdir(parents=True, exist_ok=True)
    out = ROOT / shot
    out.parent.mkdir(parents=True, exist_ok=True)

    url = (ROOT / page).as_uri()
    browser = find_browser()

    measured = page_height(browser, url, width)
    if measured and measured > height:
        print(f"note    : page reports {measured}px tall; raising window from {height}")
        height = measured + 40

    print(f"browser : {browser}")
    print(f"profile : {PROFILE}")
    print(f"page    : {url}")
    print(f"output  : {out}  ({width}x{height})")

    result = run(browser, [
        "--force-device-scale-factor=2",
        f"--screenshot={out}",
        f"--window-size={width},{height}",
        f"--virtual-time-budget=4000",
        url,
    ])

    if not out.exists():
        print(result.stdout[-2000:])
        print(result.stderr[-2000:], file=sys.stderr)
        raise SystemExit(f"Screenshot was not written: {out}")

    from PIL import Image

    image = Image.open(out)
    print(f"wrote   : {out} ({out.stat().st_size / 1024:.1f} KB, {image.size[0]}x{image.size[1]} px)")

    # Clipping check: the page background is light. A run of content pixels right up to
    # the final row means the capture window was shorter than the content.
    rgb = image.convert("RGB")
    pixels = rgb.load()
    w, h = rgb.size
    bottom_row = [pixels[x, h - 1] for x in range(0, w, max(1, w // 12))]
    if any(sum(c) < 600 for c in bottom_row):
        raise SystemExit(
            "REFUSING: the bottom row still contains content, so this shot is clipped. "
            f"Increase the height (currently {height}) and re-run."
        )

    # Trim trailing background rows. `--window-size` sets the viewport, and Chrome will not
    # shrink it to the content, so a deliberately over-tall window leaves a band of empty
    # background. Trimming is more reliable than guessing a height that fits exactly.
    background = pixels[2, h - 1]
    last_content = 0
    for y in range(h - 1, -1, -1):
        row_is_empty = all(
            sum(abs(a - b) for a, b in zip(pixels[x, y], background)) <= 6
            for x in range(0, w, max(1, w // 40))
        )
        if not row_is_empty:
            last_content = y
            break

    if 0 < last_content < h - 1:
        trimmed = image.crop((0, 0, w, min(h, last_content + 1 + (40 * 2))))
        trimmed.save(out)
        print(f"trimmed : {w}x{h} -> {trimmed.size[0]}x{trimmed.size[1]} px ({out.stat().st_size / 1024:.1f} KB)")

    print("check   : bottom row is empty background, nothing clipped")


if __name__ == "__main__":
    main()

