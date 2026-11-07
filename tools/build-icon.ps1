# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
# Файлы значка Merno из пиксельного рисунка (сетка, например 48×48 из tools\icon-scene.ps1).
# Сначала увеличение по клеткам в 32 раза, затем плавное уменьшение до нужного размера — клетки ровные при любом размере.
# Для плавного значка (картинка 1024) — tools/make-icon.py: GDI+ здесь даёт кайму и зубцы на маленьких размерах.
# Обычная картинка (сторона от 256, например 1024×1024) уменьшается плавно, без увеличения по клеткам.
# Не подменять app.ico переименованным PNG: сборка падает с CS7065 — значок собирать этим скриптом.
# Запуск: powershell -File tools\build-icon.ps1 -Src <рисунок.png> -Out <папка>
# В папке: Merno-icon-1024.png, logo.png (256), app.ico (16–256).
param([Parameter(Mandatory)][string]$Src, [Parameter(Mandatory)][string]$Out)
Add-Type -AssemblyName System.Drawing
$art = New-Object System.Drawing.Bitmap ((Resolve-Path $Src).Path)
if ($art.Width -ge 256) { $big = $art }   # уже большая картинка
else {
    $big = New-Object System.Drawing.Bitmap ($art.Width * 32), ($art.Height * 32)
    $g = [System.Drawing.Graphics]::FromImage($big); $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
    $g.DrawImage($art, 0, 0, $big.Width, $big.Height); $g.Dispose()
}
function Size($s) {
    $b = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($b); $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($big, 0, 0, $s, $s); $g.Dispose(); $b
}
New-Item -ItemType Directory -Force $Out | Out-Null
$png = [System.Drawing.Imaging.ImageFormat]::Png
(Size 1024).Save((Join-Path $Out 'Merno-icon-1024.png'), $png)
(Size 256).Save((Join-Path $Out 'logo.png'), $png)
$imgs = foreach ($s in 16, 20, 24, 32, 40, 48, 64, 96, 128, 256) { $ms = New-Object System.IO.MemoryStream; (Size $s).Save($ms, $png); ,@($s, $ms.ToArray()) }
$fs = [System.IO.File]::Create((Join-Path $Out 'app.ico')); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$imgs.Count)
$off = 6 + 16 * $imgs.Count
foreach ($i in $imgs) {
    $s = $i[0]; $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$i[1].Length); $w.Write([UInt32]$off); $off += $i[1].Length
}
foreach ($i in $imgs) { $w.Write($i[1]) }
$w.Close()
