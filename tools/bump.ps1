# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
# Номер версии ставится при выпуске, а не при каждой правке.
# Между выпусками правки копятся: в CHANGELOG.md — раздел «## Готовится», в Assets\whatsnew.json — запись "version": "next".
# Этот скрипт (его зовёт release.bat перед сборкой) превращает черновик в следующий номер:
#   X.Y.Z: обычно исправления — X.Y.(Z+1); -Minor — новое заметное — X.(Y+1).0; -Major — большая переделка — (X+1).0.0.
#   Вписывает номер в App.xaml.cs, Merno.csproj, CHANGELOG.md, whatsnew.json.
#   release.bat зовёт без ключей; для -Minor/-Major запустите bump.ps1 сами перед release.bat — черновик станет номером,
#   и release.bat его уже не тронет.
# Номер сразу коммитится («X.Y.Z: выпуск»): tools\release.ps1 ставит метку выпуска на закоммиченный код.
# Черновика нет — ничего не меняет (номер уже поставлен раньше, например с -Minor, или прошлый выпуск оборвался позже).
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File tools\bump.ps1 [-Minor | -Major] [-DryRun]
param([switch]$Minor, [switch]$Major, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Read($f) { [IO.File]::ReadAllText((Join-Path $root $f), [Text.Encoding]::UTF8) }
function Save($f, $text) {
    $p = Join-Path $root $f
    $bom = ([IO.File]::ReadAllBytes($p) | Select-Object -First 3) -join ',' -eq '239,187,191'
    [IO.File]::WriteAllText($p, $text, (New-Object System.Text.UTF8Encoding $bom))
}

$log = Read 'CHANGELOG.md'
$wn  = Read 'Assets\whatsnew.json'
$hasLog = $log -match '(?m)^## Готовится\s*$'
$hasWn  = $wn -match '"version"\s*:\s*"next"'
if (-not $hasLog -and -not $hasWn) { Write-Host '  Черновика нет, номер версии не меняю.'; exit 0 }
if (-not ($hasLog -and $hasWn)) { Write-Host '  ОШИБКА: черновик есть только в одном файле (нужны оба: CHANGELOG «## Готовится» и whatsnew "next").' -ForegroundColor Red; exit 1 }

$app = Read 'App.xaml.cs'
if ($app -notmatch 'const string Version = "(\d+)\.(\d+)\.(\d+)"') { Write-Host '  ОШИБКА: не найдена версия X.Y.Z в App.xaml.cs' -ForegroundColor Red; exit 1 }
$x = [int]$Matches[1]; $y = [int]$Matches[2]; $z = [int]$Matches[3]
$old = "$x.$y.$z"
$new = if ($Major) { "$($x + 1).0.0" } elseif ($Minor) { "$x.$($y + 1).0" } else { "$x.$y.$($z + 1)" }

Write-Host "  Версия: $old → $new" -ForegroundColor Cyan
if ($DryRun) { exit 0 }
Save 'App.xaml.cs' ($app -replace 'const string Version = "[^"]+"', "const string Version = `"$new`"")
$proj = Read 'Merno.csproj'
Save 'Merno.csproj' ($proj -replace '<Version>[^<]+</Version>', "<Version>$new</Version>")
Save 'CHANGELOG.md' ([regex]::new('(?m)^## Готовится\s*$').Replace($log, "## $new", 1))
Save 'Assets\whatsnew.json' ([regex]::new('"version"\s*:\s*"next"').Replace($wn, "`"version`": `"$new`"", 1))
Write-Host "  Номер $new вписан в App.xaml.cs, Merno.csproj, CHANGELOG.md и whatsnew.json." -ForegroundColor Green
# только эти четыре файла: остальные незакоммиченные правки в коммит выпуска не попадут
& git -C $root commit -q -m "${new}: выпуск" -- App.xaml.cs Merno.csproj CHANGELOG.md Assets/whatsnew.json
if ($LASTEXITCODE -ne 0) { Write-Host '  ОШИБКА: не удалось закоммитить номер версии (git commit).' -ForegroundColor Red; exit 1 }
Write-Host "  Коммит «${new}: выпуск» создан."
