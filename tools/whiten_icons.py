"""
Converts every non-transparent pixel in each PNG under assets/icons/ to
solid white, preserving alpha. The plugin's Image components tint these
sprites at render time, so the source assets need to be neutral white —
black source pixels would multiply against the tint and stay black.

Run this once after dropping a new icon into assets/icons/.

Usage:
    python tools/whiten_icons.py                  # default: assets/icons/
    python tools/whiten_icons.py path/to/dir      # explicit directory

Dependency: Pillow (pip install pillow).
"""

import os
import sys
from PIL import Image


def whiten(path: str) -> None:
    im = Image.open(path).convert("RGBA")
    pixels = im.load()
    w, h = im.size
    changed = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            if (r, g, b) != (255, 255, 255):
                pixels[x, y] = (255, 255, 255, a)
                changed += 1
    im.save(path, "PNG")
    print(f"  {os.path.basename(path)}: {changed} pixel(s) recolored")


def main() -> int:
    target = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)),
        "..",
        "assets",
        "icons",
    )
    target = os.path.abspath(target)
    if not os.path.isdir(target):
        print(f"Directory not found: {target}")
        return 1

    print(f"Whitening icons in {target}")
    for name in sorted(os.listdir(target)):
        if not name.lower().endswith(".png"):
            continue
        whiten(os.path.join(target, name))
    return 0


if __name__ == "__main__":
    sys.exit(main())
