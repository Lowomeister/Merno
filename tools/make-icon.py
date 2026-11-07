#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
"""Значок Merno из картинки 1024×1024: app.ico (15 размеров, 16–256) и logo.png (256).

Уменьшение — точным усреднением площади в линейном свете. GDI+ (build-icon.ps1) и Lanczos на резком
белом крае луны дают тёмную кайму и зубцы — на панели задач значок выглядел «рваным».
В ico есть 20, 30, 36, 40, 60, 72, 80 — их берёт Windows при масштабе 125–200 %; без них она
растягивает соседний размер, и края ломаются.

Нужны Pillow и numpy: pip install pillow numpy
Запуск:  python tools/make-icon.py docs/images/Merno-icon-1024.png Assets
         (в папке появятся app.ico и logo.png)
"""
import io
import os
import struct
import sys

if os.environ.get("MERNO_PYLIB"):          # библиотеки в отдельной папке (pip install --target …)
    sys.path.insert(0, os.environ["MERNO_PYLIB"])
import numpy as np
from PIL import Image

SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256]


def load(path):
    a = np.asarray(Image.open(path).convert("RGBA")).astype(np.float64) / 255
    # линейный свет и предумноженная альфа — иначе края темнеют
    return np.concatenate([a[..., :3] ** 2.2 * a[..., 3:4], a[..., 3:4]], 2)


def weights(n, s):
    """Матрица s×n: каждый пиксель результата — среднее покрытого им куска исходника."""
    edges = np.linspace(0, n, s + 1)
    w = np.zeros((s, n))
    for i in range(s):
        a, b = edges[i], edges[i + 1]
        for j in range(int(np.floor(a)), int(np.ceil(b))):
            w[i, j] = min(b, j + 1) - max(a, j)
        w[i] /= w[i].sum()
    return w


def shrink(arr, s):
    wy, wx = weights(arr.shape[0], s), weights(arr.shape[1], s)
    return np.stack([wy @ arr[..., c] @ wx.T for c in range(4)], 2)


def to_image(out):
    alpha = out[..., 3:4]
    col = np.where(alpha > 1e-5, out[..., :3] / np.maximum(alpha, 1e-5), 0) ** (1 / 2.2)
    px = np.clip(np.concatenate([col, alpha], 2), 0, 1) * 255 + 0.5
    return Image.fromarray(px.astype(np.uint8), "RGBA")


def png_bytes(im):
    b = io.BytesIO()
    im.save(b, "PNG", optimize=True)
    return b.getvalue()


def main(src, out_dir):
    arr = load(src)
    imgs = [(s, png_bytes(to_image(shrink(arr, s)))) for s in SIZES]
    head = struct.pack("<HHH", 0, 1, len(imgs))
    off, dirs = 6 + 16 * len(imgs), b""
    for s, data in imgs:
        dirs += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), off)
        off += len(data)
    with open(os.path.join(out_dir, "app.ico"), "wb") as f:
        f.write(head + dirs + b"".join(d for _, d in imgs))
    to_image(shrink(arr, 256)).save(os.path.join(out_dir, "logo.png"), "PNG", optimize=True)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
