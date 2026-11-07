# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
# Проверка перед выпуском: всё ли сходится. Ничего не меняет.
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File tools\check.ps1 [-NoBuild] [-NoTests]
# Проверяет: версия одинакова в App.xaml.cs, Merno.csproj, CHANGELOG.md, whatsnew.json и в формате X.Y.Z;
# JSON-файлы читаются; в каталоге нет Яндекса; программа собирается (без -NoBuild); тесты проходят (без -NoTests).
param([switch]$NoBuild, [switch]$NoTests)
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$bad = 0
function Ok($t) { Write-Host "  ✓ $t" -ForegroundColor Green }
function No($t) { Write-Host "  ✗ $t" -ForegroundColor Red; $script:bad++ }
function Read($f) { [IO.File]::ReadAllText((Join-Path $root $f), [Text.Encoding]::UTF8) }

Write-Host ''; Write-Host '  Проверка Merno' -ForegroundColor Cyan

# 1. версия в четырёх местах
$v = if ((Read 'App.xaml.cs') -match 'const string Version = "([^"]+)"') { $Matches[1] } else { $null }
if (-not $v) { No 'не найдена версия в App.xaml.cs' } else {
    $proj = if ((Read 'Merno.csproj') -match '<Version>([^<]+)</Version>') { $Matches[1] }
    # между выпусками сверху черновик: CHANGELOG «## Готовится», whatsnew "next" — номер ставит tools\bump.ps1 при выпуске;
    # тогда сверяем с последней выпущенной записью под черновиком
    # @(...): при одной записи PowerShell отдаёт строку, и [0] было бы первой буквой
    $logs = @([regex]::Matches((Read 'CHANGELOG.md'), '(?m)^## (\S+)') | ForEach-Object { $_.Groups[1].Value })
    $wns  = @([regex]::Matches((Read 'Assets\whatsnew.json'), '"version"\s*:\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    $draftLog = $logs[0] -eq 'Готовится'; $draftWn = $wns[0] -eq 'next'
    $log = if ($draftLog) { $logs[1] } else { $logs[0] }
    $wn  = if ($draftWn) { $wns[1] } else { $wns[0] }
    if ($draftLog -ne $draftWn) { No 'черновик есть только в одном файле: нужны и «## Готовится» в CHANGELOG, и "next" в whatsnew' }
    if ($proj -eq $v -and $log -eq $v -and $wn -eq $v) { Ok ("версия $v везде одинаковая" + $(if ($draftLog) { ' (есть черновик следующей)' } else { '' })) }
    else { No "версии расходятся: App.xaml.cs $v, Merno.csproj $proj, CHANGELOG $log, whatsnew $wn" }
    # формат: X.Y.Z
    if ($v -match '^\d+\.\d+\.\d+$') { Ok 'версия в формате X.Y.Z' }
    else { No "версия $v не в формате X.Y.Z" }
}

# 2. JSON читается
foreach ($j in 'Assets\whatsnew.json', 'Assets\apps.json') {
    try { $null = (Read $j) | ConvertFrom-Json; Ok "$j читается" } catch { No "$j не читается: $($_.Exception.Message)" }
}

# 3. жёсткие правила каталога
if ((Read 'Assets\apps.json') -match '(?i)yandex|яндекс') { No 'в apps.json есть Яндекс' } else { Ok 'в каталоге нет Яндекса' }

# 4. сборка и тесты
$out = Join-Path $env:TEMP 'merno-check'
if (-not $NoBuild) {
    dotnet build Merno.csproj -c Release -o $out -v q -nologo 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Ok 'программа собирается' } else { No 'программа не собирается: запустите build.bat, там будет видна ошибка' }
}
if (-not $NoTests) {
    # итог берём из файла результатов (trx), а не из текста в консоли: тот зависит от языка и кодировки
    # (на русской Windows «пройдено» приходит искажённым)
    # dotnet test завершается без ошибки, даже если ни один тест не запустился (например, Smart App Control
    # заблокировал неподписанную сборку тестов): требуем, чтобы прошёл хотя бы один. Заблокирован Debug —
    # пробуем Release: это другой файл, и Windows часто пропускает его
    $ran = $false
    foreach ($cfg in 'Debug', 'Release') {
        $trxDir = Join-Path $out "tests-$cfg"
        Remove-Item -LiteralPath $trxDir -Recurse -Force -ErrorAction SilentlyContinue
        dotnet test Merno.Tests\Merno.Tests.csproj -c $cfg -v q -nologo --logger "trx;LogFileName=check.trx" --results-directory $trxDir 2>&1 | Out-Null
        $testCode = $LASTEXITCODE
        $trx = Join-Path $trxDir 'check.trx'
        if (Test-Path -LiteralPath $trx) {
            try { $ran = [int]([xml](Get-Content -LiteralPath $trx -Raw -Encoding UTF8)).TestRun.ResultSummary.Counters.passed -gt 0 } catch { }
        }
        if ($testCode -ne 0 -or $ran) { break }
    }
    if ($testCode -eq 0 -and $ran) { Ok 'тесты проходят' }
    elseif ($testCode -eq 0) { No 'тесты не запустились (возможно, Windows заблокировала сборку тестов): dotnet test Merno.Tests\Merno.Tests.csproj' }
    else { No 'тесты не проходят: dotnet test Merno.Tests\Merno.Tests.csproj' }
}

Write-Host ''
if ($bad -gt 0) { Write-Host "  Найдено проблем: $bad" -ForegroundColor Red; exit 1 }
Write-Host '  Всё в порядке' -ForegroundColor Green; exit 0
