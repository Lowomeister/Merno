# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
# Пять вариантов значка Merno в стиле пиксельного баннера Главной (MainWindow.Banner.cs):
# небо полосами, сияние двумя кругами, серп с тенью и кратерами, горы с гребнем, горизонт в цвет акцента,
# отражение луны полосками через ряд. Палитра — базовая бирюзовая из баннера.
# Запуск: powershell -File tools\icon-scene.ps1 -Out <папка> [-Grid 48]
param([Parameter(Mandatory)][string]$Out, [int]$Grid = 48)
Add-Type -AssemblyName System.Drawing
$N = $Grid
function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function Mix($a, $b, $t) { [System.Drawing.Color]::FromArgb(255, [int]($a.R + ($b.R - $a.R) * $t), [int]($a.G + ($b.G - $a.G) * $t), [int]($a.B + ($b.B - $a.B) * $t)) }
# полосы как в баннере: 4 ступени между двумя цветами
function Band($top, $low, $y, $y0, $y1) { $t = if ($y1 -le $y0) { 0 } else { ($y - $y0) / [double]($y1 - $y0) }; Mix $top $low ([Math]::Floor($t * 4) / 4) }
function P($x, $y, $c) { $x = [int]$x; $y = [int]$y; if ($x -ge 0 -and $y -ge 0 -and $x -lt $N -and $y -lt $N) { $script:bmp.SetPixel($x, $y, $c) } }
function G($x, $y) { $script:bmp.GetPixel([int]$x, [int]$y) }

$skyTop = C '#162224'; $skyLow = C '#24393B'; $glow = C '#5CB8A7'
$farTop = C '#3A5A56'; $farLow = C '#1E2C2E'; $farRidge = C '#4E7570'
$nearC = C '#141E20'; $nearRidge = C '#22312F'
$waterTop = C '#1C2D2E'; $waterLow = C '#101719'
$moon = C '#E8F4F0'; $moonShade = C '#B9D0CA'; $moonDark = C '#93AFA8'
$star = C '#CFE2DD'; $starDim = C '#6F8D88'; $refl = C '#D6ECE7'; $ripple = C '#3E5654'

function Sky($hz, $mx, $my, $g1, $g2) {
    for ($y = 0; $y -lt $hz; $y++) { for ($x = 0; $x -lt $N; $x++) {
        $c = Band $skyTop $skyLow $y 0 ($hz - 1)
        $d = [Math]::Sqrt(($x - $mx + 0.5) * ($x - $mx + 0.5) + ($y - $my + 0.5) * ($y - $my + 0.5))
        if ($d -le $g1) { $c = Mix $c $glow 0.16 } elseif ($d -le $g2) { $c = Mix $c $glow 0.07 }
        P $x $y $c
    }}
}
function Stars($list) { foreach ($s in $list) {
    P $s[0] $s[1] ($(if ($s.Count -gt 2) { $star } else { $starDim }))
    if ($s.Count -gt 3) { foreach ($o in @(@(-1,0),@(1,0),@(0,-1),@(0,1))) { P ($s[0] + $o[0]) ($s[1] + $o[1]) $starDim } }
}}
# серп как в баннере: круг r минус круг, сдвинутый на (0.45r, -0.33r)
function Moon($mx, $my, $r) {
    $ox = $mx + $r * 0.45; $oy = $my - $r * 0.33; $rc = $r * 0.89
    for ($y = [int]($my - $r - 1); $y -le $my + $r + 1; $y++) { for ($x = [int]($mx - $r - 1); $x -le $mx + $r + 1; $x++) {
        $dx = $x - $mx + 0.5; $dy = $y - $my + 0.5; $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
        $cx = $x - $ox + 0.5; $cy = $y - $oy + 0.5; $dc = [Math]::Sqrt($cx * $cx + $cy * $cy)
        if ($d -gt $r -or $dc -lt $rc) { continue }
        $c = if ($dc -lt $rc + 1.3) { $moonShade } else { $moon }
        if ($d -gt $r - 0.8 -and $dx -lt 0) { $c = $moonShade }
        P $x $y $c
    }}
    foreach ($k in @(@(-0.66,0.11),@(-0.55,0.22),@(-0.33,0.66),@(-0.77,-0.33))) {
        $px = [int]($mx + $k[0] * $r); $py = [int]($my + $k[1] * $r)
        if ((G $px $py).R -gt 150) { P $px $py $moonDark }
    }
}
function Height($x, $pts) {
    for ($i = 1; $i -lt $pts.Count; $i++) { if ($x -le $pts[$i][0]) {
        $x0 = $pts[$i-1][0]; $y0 = $pts[$i-1][1]; $x1 = $pts[$i][0]; $y1 = $pts[$i][1]
        return $y0 + ($y1 - $y0) * ($x - $x0) / [double][Math]::Max(1, $x1 - $x0) } }
    return $pts[$pts.Count - 1][1]
}
# горы: точки заданы в долях (0..1) ширины и высоты, чтобы не зависеть от сетки
function Mountains($pts, $hz, $near) {
    $abs = foreach ($p in $pts) { ,@(($p[0] * $N), ($p[1] * $N)) }
    for ($x = 0; $x -lt $N; $x++) {
        $top = [int][Math]::Round((Height $x $abs)); if ($top -ge $hz) { continue }
        for ($y = $top; $y -lt $hz; $y++) {
            if ($near) { P $x $y ($(if ($y -eq $top) { $nearRidge } else { $nearC })) }
            else { P $x $y ($(if ($y -eq $top) { $farRidge } else { Band $farTop $farLow $y $top ($hz - 1) })) }
        }
    }
}
function Water($hz, $mx, $width) {
    for ($y = $hz; $y -lt $N; $y++) { for ($x = 0; $x -lt $N; $x++) { P $x $y (Band $waterTop $waterLow $y $hz ($N - 1)) } }
    for ($x = 0; $x -lt $N; $x++) { P $x $hz (Mix $waterTop $glow 0.45) }
    # рябь
    $rnd = New-Object System.Random 42
    for ($i = 0; $i -lt [int]($N / 4); $i++) {
        $y = $hz + 3 + $rnd.Next([Math]::Max(1, $N - $hz - 4)); $len = 2 + $rnd.Next([int]($N / 10)); $x0 = $rnd.Next($N)
        for ($k = 0; $k -lt $len; $k++) { $xx = $x0 + $k; if ([Math]::Abs($xx - $mx) -ge $width / 2 + 2) { P $xx $y $ripple } }
    }
    # отражение: полоски через ряд, ниже — уже и бледнее
    $row = 0
    for ($y = $hz + 2; $y -lt $N - 1; $y += 2) {
        $depth = ($y - $hz) / [double]($N - $hz)
        $half = [Math]::Max(1, [int][Math]::Round(($width - $depth * $width * 0.55) / 2 * (0.8 + 0.25 * [Math]::Sin($row * 1.3))))
        $a = 0.85 - $depth * 0.7
        for ($x = $mx - $half; $x -le $mx + $half; $x++) {
            $t = if ($x -eq $mx - $half -or $x -eq $mx + $half) { $a * 0.5 } else { $a }
            P $x $y (Mix (G $x $y) $refl $t)
        }
        $row++
    }
}
function Mask($shape, $r) {
    $clear = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
    for ($y = 0; $y -lt $N; $y++) { for ($x = 0; $x -lt $N; $x++) {
        if ($shape -eq 'circle') { $cx = $N / 2; $cy = $N / 2; $rr = $N / 2 }
        else { $rr = $r; $cx = [Math]::Min([Math]::Max($x + 0.5, $r), $N - $r); $cy = [Math]::Min([Math]::Max($y + 0.5, $r), $N - $r) }
        if ([Math]::Sqrt(($x + 0.5 - $cx) * ($x + 0.5 - $cx) + ($y + 0.5 - $cy) * ($y + 0.5 - $cy)) -gt $rr) { $script:bmp.SetPixel($x, $y, $clear) }
    }}
}
function F($v) { [int][Math]::Round($v * $N) }   # доля сетки → клетки

# слои волн (как в старом значке): каждая волна закрашивает всё ниже себя, гребень светлее
function Waves($layers) {
    foreach ($w in $layers) {
        for ($x = 0; $x -lt $N; $x++) {
            $top = [int][Math]::Round($N * ($w[0] + $w[1] * [Math]::Sin(2 * [Math]::PI * ($x / $N * $w[2] + $w[3]))))
            for ($y = [Math]::Max(0, $top); $y -lt $N; $y++) { P $x $y ($(if ($y -eq $top) { $w[5] } else { $w[4] })) }
        }
    }
}

# во всех вариантах луна слева внизу и тонет: вода/волна рисуется поверх нижней части серпа
$designs = [ordered]@{
    # 1. Луна садится в озеро: звёздное небо, горизонт низко, отражение под луной
    '1-zakat' = {
        $hz = F 0.72; $mx = F 0.32; $my = F 0.56
        Sky $hz $mx $my (F 0.28) (F 0.38)
        Stars @(@((F 0.62),(F 0.14),1,1),@((F 0.86),(F 0.3),1),@((F 0.4),(F 0.1)),@((F 0.12),(F 0.22)),@((F 0.78),(F 0.52)),@((F 0.9),(F 0.08),1))
        Moon $mx $my (F 0.2)
        Water $hz $mx (F 0.26)
        Mask 'round' (F 0.16)
    }
    # 2. То же + горы справа: луна слева тонет, справа дальний хребет
    '2-gory' = {
        $hz = F 0.72; $mx = F 0.3; $my = F 0.57
        Sky $hz $mx $my (F 0.26) (F 0.35)
        Stars @(@((F 0.56),(F 0.12),1,1),@((F 0.84),(F 0.1),1),@((F 0.16),(F 0.18)),@((F 0.42),(F 0.3)))
        Moon $mx $my (F 0.19)
        Mountains @(@(0,0.72),@(0.5,0.72),@(0.62,0.5),@(0.72,0.58),@(0.86,0.4),@(1,0.52)) $hz $false
        Mountains @(@(0,0.72),@(0.62,0.72),@(0.76,0.62),@(0.9,0.66),@(1,0.6)) $hz $true
        Water $hz $mx (F 0.24)
        Mask 'round' (F 0.16)
    }
    # 3. Старый значок в цветах баннера: ночные волны слоями, луна тонет в нижней
    '3-volny' = {
        Sky (F 1) (F 0.3) (F 0.6) (F 0.3) (F 0.4)
        Stars @(@((F 0.6),(F 0.1),1,1),@((F 0.86),(F 0.16),1),@((F 0.3),(F 0.12)),@((F 0.74),(F 0.3)))
        Waves @(@(0.34,0.04,1,0.1,(Mix $skyLow $farTop 0.35),(Mix $skyLow $farRidge 0.4)),@(0.48,0.045,1.2,0.6,(Mix $skyLow $farTop 0.6),$farRidge))
        Moon (F 0.3) (F 0.6) (F 0.19)
        Waves @(@(0.76,0.05,1,0.3,$waterTop,(Mix $waterTop $glow 0.45)),@(0.88,0.04,1.4,0.8,$waterLow,(Mix $waterLow $glow 0.3)))
        Mask 'round' (F 0.16)
    }
    # 4. Крупно: большая луна в углу, наполовину в воде, справа пусто и звёзды
    '4-krupno' = {
        $hz = F 0.7; $mx = F 0.34; $my = F 0.5
        Sky $hz $mx $my (F 0.36) (F 0.48)
        Stars @(@((F 0.72),(F 0.14),1,1),@((F 0.88),(F 0.36),1),@((F 0.54),(F 0.06)),@((F 0.16),(F 0.1)))
        Moon $mx $my (F 0.27)
        Water $hz $mx (F 0.3)
        Mask 'round' (F 0.16)
    }
    # 5. Круглый: луна слева внизу садится в воду, низкие горы на горизонте
    '5-krug' = {
        $hz = F 0.7; $mx = F 0.32; $my = F 0.55
        Sky $hz $mx $my (F 0.26) (F 0.35)
        Stars @(@((F 0.56),(F 0.18),1,1),@((F 0.78),(F 0.28),1),@((F 0.36),(F 0.16)))
        Moon $mx $my (F 0.19)
        Mountains @(@(0,0.7),@(0.54,0.7),@(0.66,0.58),@(0.78,0.62),@(0.92,0.54),@(1,0.6)) $hz $false
        Water $hz $mx (F 0.24)
        Mask 'circle' 0
    }
}

New-Item -ItemType Directory -Force $Out | Out-Null
$png = [System.Drawing.Imaging.ImageFormat]::Png
function Scale($b, $size, $smooth) {
    $r = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($r); $g.InterpolationMode = if ($smooth) { 'HighQualityBicubic' } else { 'NearestNeighbor' }; $g.PixelOffsetMode = 'Half'
    $g.DrawImage($b, 0, 0, $size, $size); $g.Dispose(); $r
}
$made = @()
foreach ($k in $designs.Keys) {
    $script:bmp = New-Object System.Drawing.Bitmap $N, $N
    & $designs[$k]
    $bmp.Save((Join-Path $Out "$k-$N.png"), $png)
    (Scale $bmp 528 $false).Save((Join-Path $Out "$k.png"), $png)
    $made += ,@($k, $bmp)
}
# общий лист: крупно, а под ним — как на панели задач (32) и в трее (16)
$cw = 300; $sheet = New-Object System.Drawing.Bitmap ($cw * $made.Count), 370
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear((C '#0E1214'))
$font = New-Object System.Drawing.Font 'Segoe UI', 12; $brush = New-Object System.Drawing.SolidBrush (C '#A8B5B2')
$i = 0
foreach ($m in $made) {
    $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
    $g.DrawImage($m[1], $i * $cw + 22, 16, 264, 264)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.DrawImage($m[1], $i * $cw + 22, 300, 32, 32)
    $g.DrawImage($m[1], $i * $cw + 66, 308, 16, 16)
    $g.DrawString($m[0], $font, $brush, $i * $cw + 96, 304)
    $i++
}
$g.Dispose(); $sheet.Save((Join-Path $Out 'all.png'), $png)

