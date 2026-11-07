# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
# Сборка: установщик, портативная версия и комплект обновления в dist\. Ничего не выпускает и не подписывает.
# Нужны .NET 10 SDK (версия — в global.json), Python 3.9 или новее, Inno Setup 6 и интернет (Zig и пакеты NuGet).
param([switch]$NoPause)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
# при ошибке тоже ждём Enter: иначе окно build.bat закроется, и ошибку не увидеть
function Die($t) {
    Write-Host "  ОШИБКА: $t" -ForegroundColor Red
    if (-not $NoPause) { Read-Host 'Нажмите Enter, чтобы закрыть' }
    exit 1
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Die 'не найден .NET 10 SDK (команда dotnet)' }
if (-not (Get-Command python -ErrorAction SilentlyContinue)) { Die 'не найден Python 3.9 или новее (команда python)' }
& dotnet restore Merno.csproj -nologo
if ($LASTEXITCODE -ne 0) { Die 'не удалось получить пакеты (dotnet restore)' }
& python tools/native/get-zig.py
if ($LASTEXITCODE -ne 0) { Die 'не удалось подготовить компилятор Zig' }
& python tools/native/package.py
if ($LASTEXITCODE -ne 0) { Die 'сборка не удалась, причина — выше' }
& python tools/native/smoke.py
if ($LASTEXITCODE -ne 0) { Die 'собранные файлы не прошли проверку, причина — выше' }
Write-Host 'Готово: dist\Merno-Setup.exe (установщик), dist\Merno-Portable.zip (портативная), dist\Merno-Setup.zip (для обновления по воздуху).'
if (-not $NoPause) { Read-Host 'Нажмите Enter, чтобы закрыть' }
