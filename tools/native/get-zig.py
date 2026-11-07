# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
"""Pinned build dependency. Downloads only the official archive; verifies SHA-256 before extraction."""
import hashlib
from pathlib import Path
import urllib.request
import zipfile

root = Path(__file__).resolve().parents[2] / "obj" / "native-tools"
root.mkdir(parents=True, exist_ok=True)
archive = root / "zig.zip"
expected = "3a0ed1e8799a2f8ce2a6e6290a9ff22e6906f8227865911fb7ddedc3cc14cb0c"
if not archive.exists() or hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
    urllib.request.urlretrieve("https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip", archive)
if hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
    raise SystemExit("Архив Zig не совпадает по SHA-256: файл повреждён или подменён")
with zipfile.ZipFile(archive) as package:
    for name in package.namelist():
        if not (root / name).resolve().is_relative_to(root.resolve()):
            raise SystemExit("В архиве Zig есть путь за пределами папки: распаковка отменена")
    package.extractall(root)
print("Компилятор Zig проверен и готов")
