#!/usr/bin/env python3
"""Generates MacExtend's .ico files without third-party packages.

The mark mirrors the macOS "Arrange displays" pane: the Mac's main display (with its
white menu bar strip) on the left and the Windows laptop as a second display touching
its right edge.

Shapes are drawn as signed distance fields, which gives clean anti-aliasing at 16 px.
Small sizes are stored as 32-bit BMP frames (System.Drawing.Icon, used by the tray,
reads those reliably); 256 px is stored as PNG.

Usage: python3 tools/make_icons.py src/MacExtend.App/Assets
"""
import math
import os
import struct
import sys
import zlib

SLATE_TOP = (0x26, 0x32, 0x44)
SLATE_BOTTOM = (0x17, 0x1E, 0x29)
MAC_GREY = (0xC9, 0xD1, 0xDC)
MENU_WHITE = (0xFF, 0xFF, 0xFF)
ACCENT = (0x3D, 0x7B, 0xFA)

STATUS = {
    "ready": (0x4C, 0x8D, 0xFF),
    "connected": (0x34, 0xC2, 0x7A),
    "error": (0xF0, 0x52, 0x4F),
}
GLYPH = {"dark": (0xFF, 0xFF, 0xFF), "light": (0x1A, 0x1D, 0x21)}


def rounded_rect(cx, cy, hw, hh, r):
    def sdf(x, y):
        qx = abs(x - cx) - (hw - r)
        qy = abs(y - cy) - (hh - r)
        outside = math.hypot(max(qx, 0.0), max(qy, 0.0))
        return outside + min(max(qx, qy), 0.0) - r
    return sdf


def circle(cx, cy, r):
    return lambda x, y: math.hypot(x - cx, y - cy) - r


def stroke(sdf, width):
    return lambda x, y: abs(sdf(x, y)) - width / 2.0


def rect(x0, y0, x1, y1, r=0.0):
    return rounded_rect((x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2, r)


class Canvas:
    def __init__(self, size):
        self.size = size
        self.px = [[0.0, 0.0, 0.0, 0.0] for _ in range(size * size)]  # premultiplied RGBA 0..1

    def _coverage(self, sdf, x, y):
        # sdf in unit space; convert to pixels for a one-pixel-wide antialiasing ramp
        d = sdf((x + 0.5) / self.size, (y + 0.5) / self.size) * self.size
        return min(1.0, max(0.0, 0.5 - d))

    def fill(self, sdf, color, alpha=1.0, color_at=None):
        for y in range(self.size):
            for x in range(self.size):
                a = self._coverage(sdf, x, y) * alpha
                if a <= 0.0:
                    continue
                c = color_at(y / self.size) if color_at else color
                p = self.px[y * self.size + x]
                for i in range(3):
                    p[i] = c[i] / 255.0 * a + p[i] * (1.0 - a)
                p[3] = a + p[3] * (1.0 - a)

    def erase(self, sdf):
        for y in range(self.size):
            for x in range(self.size):
                a = self._coverage(sdf, x, y)
                if a > 0.0:
                    p = self.px[y * self.size + x]
                    for i in range(4):
                        p[i] *= 1.0 - a

    def rgba_rows(self):
        """Straight-alpha RGBA bytes, top row first."""
        rows = []
        for y in range(self.size):
            row = bytearray()
            for x in range(self.size):
                r, g, b, a = self.px[y * self.size + x]
                if a > 0.0:
                    r, g, b = r / a, g / a, b / a
                row += bytes(int(round(min(1.0, max(0.0, v)) * 255)) for v in (r, g, b, a))
            rows.append(bytes(row))
        return rows


def lerp(c0, c1, t):
    return tuple(int(round(a + (b - a) * t)) for a, b in zip(c0, c1))


def draw_app(size):
    c = Canvas(size)
    c.fill(rect(0.02, 0.02, 0.98, 0.98, 0.22), None, color_at=lambda t: lerp(SLATE_TOP, SLATE_BOTTOM, t))
    # Mac main display (16:10) with its menu bar strip
    c.fill(rect(0.10, 0.355, 0.56, 0.645, 0.03), MAC_GREY)
    c.fill(rect(0.10, 0.355, 0.56, 0.405, 0.03), MENU_WHITE)
    c.fill(rect(0.10, 0.385, 0.56, 0.405), MENU_WHITE)
    # Windows laptop (16:9) as the second display, top-aligned and touching
    c.fill(rect(0.585, 0.355, 0.90, 0.532, 0.03), ACCENT)
    # laptop deck under the second display
    c.fill(rect(0.565, 0.55, 0.92, 0.575, 0.0125), MAC_GREY, alpha=0.9)
    return c


def draw_tray(size, state, taskbar):
    g = GLYPH[taskbar]
    c = Canvas(size)
    alpha = 0.55 if state == "stopped" else 1.0
    line = max(1.25 / size, 0.085) if size <= 20 else 0.075
    # Mac display outline with filled menu bar
    mac = rect(0.04, 0.14, 0.56, 0.62, 0.06)
    c.fill(stroke(mac, line), g, alpha)
    c.fill(rect(0.04, 0.14, 0.56, 0.27, 0.06), g, alpha)
    # second display: outline while waiting, filled once a Mac uses it
    second = rect(0.60, 0.14, 0.96, 0.48, 0.06)
    if state == "connected":
        c.fill(second, g, alpha)
    else:
        c.fill(stroke(second, line), g, alpha)
    if state in STATUS:
        c.erase(circle(0.78, 0.78, 0.25))
        c.fill(circle(0.78, 0.78, 0.185), STATUS[state])
    return c


def png_bytes(canvas):
    raw = b"".join(b"\x00" + row for row in canvas.rgba_rows())

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", canvas.size, canvas.size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def bmp_bytes(canvas):
    s = canvas.size
    header = struct.pack("<IiiHHIIiiII", 40, s, s * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = bytearray()
    for row in reversed(canvas.rgba_rows()):  # bottom-up BGRA
        for i in range(0, len(row), 4):
            r, g, b, a = row[i:i + 4]
            pixels += bytes((b, g, r, a))
    mask_row = ((s + 31) // 32) * 4
    mask = bytes(mask_row * s)  # alpha channel decides transparency; AND mask all zero
    return header + bytes(pixels) + mask


def write_ico(path, canvases):
    frames = [png_bytes(c) if c.size >= 256 else bmp_bytes(c) for c in canvases]
    out = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    for c, data in zip(canvases, frames):
        dim = 0 if c.size >= 256 else c.size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, "wb") as f:
        f.write(out + b"".join(frames))


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)
    write_ico(os.path.join(out_dir, "app.ico"), [draw_app(s) for s in (16, 20, 24, 32, 40, 48, 64, 256)])
    with open(os.path.join(out_dir, "app-256.png"), "wb") as f:
        f.write(png_bytes(draw_app(256)))
    for taskbar in ("dark", "light"):
        for state in ("stopped", "ready", "connected", "error"):
            name = "tray-%s-%s.ico" % (state, taskbar)
            write_ico(os.path.join(out_dir, name), [draw_tray(s, state, taskbar) for s in (16, 20, 24, 32, 40, 48)])
    print("icons written to", out_dir)


if __name__ == "__main__":
    main()
