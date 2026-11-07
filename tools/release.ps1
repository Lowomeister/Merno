# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
# Выпуск Merno на GitHub Releases (github.com/Lowomeister/Merno).
# Берёт dist\Merno-Setup.zip (комплект обновления), подписывает закрытым ключом, пишет dist\version.json
# и создаёт выпуск с номером версии: Merno-<версия>.bin, version.json, Merno-Setup.exe, Merno-Portable.zip.
# Нужен gh (winget install GitHub.cli) с входом: gh auth login. Репозиторий должен быть открытым.
# Метка выпуска ставится на проверенный локальный коммит; если он ещё не на GitHub, скрипт сам делает git push.
# Описание на странице выпуска — раздел этой версии из CHANGELOG.md; -Notes — короткое описание для version.json.
# Подписанный выпуск: положите файлы из артефакта Merno-signed в dist\ и запустите этот скрипт напрямую
# с -RequireSigned (release.bat пересобирает dist\ и заменил бы подписанные файлы неподписанными).
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 [-Notes "..."] [-RequireSigned] [-NoUpload]
param(
    [string]$Repo = 'Lowomeister/Merno',
    [string]$Notes = '',
    [string]$KeyFile = (Join-Path $env:USERPROFILE '.merno\update-private.xml'),
    [switch]$RequireSigned,   # не выпускать без подписи Authenticode (комплект из GitHub Actions)
    [switch]$NoUpload
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

function Fail($t) { Write-Host "  ОШИБКА: $t" -ForegroundColor Red; exit 1 }

# 0. проверка: версии, JSON, тесты (tools\check.ps1). Программу уже собрал build.bat — второй раз не собираем.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check.ps1') -NoBuild
if ($LASTEXITCODE -ne 0) { Fail 'проверка не пройдена: исправьте и запустите выпуск снова' }

# 1. версия
$appCs = Get-Content -LiteralPath (Join-Path $root 'App.xaml.cs') -Raw -Encoding UTF8
if ($appCs -notmatch 'const string Version = "(\d+\.\d+\.\d+)"') { Fail 'не найдена версия X.Y.Z в App.xaml.cs' }
$ver = $Matches[1]
Write-Host ''
Write-Host "  Выпуск Merno $ver" -ForegroundColor Cyan

# 2. файлы
$exe = Join-Path $root 'dist\Merno-Setup.zip'
if (-not (Test-Path -LiteralPath $exe)) { Fail 'нет dist\Merno-Setup.zip: сначала запустите build.bat' }
if (-not (Test-Path -LiteralPath $KeyFile)) { Fail "нет закрытого ключа $KeyFile. Сначала запустите tools\keygen.ps1" }
# номер и подпись — по установщику внутри комплекта (так же работает с файлами, подписанными на GitHub)
$check = Join-Path $root 'obj\release-check'
if (Test-Path -LiteralPath $check) { Remove-Item -LiteralPath $check -Recurse -Force }
New-Item -ItemType Directory -Path $check | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($exe, $check)
$setup = Join-Path $check 'Merno.Setup.exe'
if (-not (Test-Path -LiteralPath $setup)) { Fail 'в комплекте нет Merno.Setup.exe' }
if (@(Get-ChildItem -LiteralPath $check -Recurse -Force).Count -ne 1) { Fail 'в комплекте лишние файлы: там должен быть только Merno.Setup.exe' }
# установщик для первой установки и установщик в комплекте обновления должны быть одним и тем же файлом
$standalone = Join-Path $root 'dist\Merno-Setup.exe'
if (-not (Test-Path -LiteralPath $standalone)) { Fail 'нет dist\Merno-Setup.exe' }
if ((Get-FileHash -LiteralPath $standalone -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash) {
    Fail 'dist\Merno-Setup.exe и установщик в dist\Merno-Setup.zip разные: положите файлы из одной сборки'
}
# Inno Setup хранит строку версии в поле фиксированной длины и добивает её пробелами: без Trim() «1.0.0   » != «1.0.0»
$fv = ([string](Get-Item -LiteralPath $setup).VersionInfo.ProductVersion).Trim()
if ($fv -ne $ver) { Fail "версия комплекта ($fv) не совпадает с исходниками ($ver); пересоберите комплект" }
$sig = Get-AuthenticodeSignature -LiteralPath $setup
if ($sig.Status -eq 'Valid') { Write-Host "  Подпись установщика: $($sig.SignerCertificate.Subject)" -ForegroundColor Green }
elseif ($RequireSigned) { Fail 'установщик не подписан (нужен комплект из GitHub Actions после SignPath)' }
else { Write-Host '  Внимание: установщик не подписан. Windows 11 может его заблокировать.' -ForegroundColor Yellow }

# 3. ключ и сверка с открытым ключом в программе
$csp = New-Object System.Security.Cryptography.CspParameters 24
$csp.Flags = [System.Security.Cryptography.CspProviderFlags]::CreateEphemeralKey
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider $csp
$rsa.PersistKeyInCsp = $false
$rsa.FromXmlString([System.IO.File]::ReadAllText($KeyFile))
$keyCs = Get-Content -LiteralPath (Join-Path $root 'Core\UpdateKey.cs') -Raw -Encoding UTF8
$myMod = ([xml]$rsa.ToXmlString($false)).RSAKeyValue.Modulus
if ($keyCs -notmatch [regex]::Escape("<Modulus>$myMod</Modulus>")) {
    Fail 'закрытый ключ не подходит к открытому ключу в Core\UpdateKey.cs. Программа не примет такое обновление.'
}

# 4. хеш, размер, подписи
$bytes = [System.IO.File]::ReadAllBytes($exe)
$size = $bytes.Length
$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$sigExe = [Convert]::ToBase64String($rsa.SignData($bytes, 'SHA256'))
$published = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
# 90 дней (UpdateMetadata.MaxLifetime). По HTTPS с GitHub истёкший срок не мешает (UpdateMetadata.Accept),
# так что долгий перерыв между выпусками ничего не ломает.
$expires = $published + 90 * 86400

# 5. описание. Раздел этой версии из CHANGELOG.md целиком — на страницу выпуска;
#    коротко (из -Notes или два первых пункта раздела) — в version.json, его Merno показывает при обновлении
$section = @(); $in = $false
foreach ($l in (Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Encoding UTF8)) {
    if ($l -match '^## ') { if ($in) { break }; $in = ($l.Trim() -eq "## $ver"); continue }
    if ($in) { $section += $l }
}
$pageNotes = ($section -join "`n").Trim()
if (-not $pageNotes) { Fail "в CHANGELOG.md нет раздела «## $ver»" }
if (-not $Notes) {
    $items = @($section | Where-Object { $_ -match '^\s*-\s+(.+)$' } | ForEach-Object { $_ -replace '^\s*-\s+', '' } | Select-Object -First 2)
    $Notes = ($items -join ' ')
}
if ($Notes.Length -gt 280) { $Notes = $Notes.Substring(0, 277) + '...' }

$Notes = $Notes.Trim()
$meta = "v2|$ver|Merno-$ver.bin|$sha|$size|installed-kit-v1|$published|$expires|$Notes"
$sigMeta = [Convert]::ToBase64String($rsa.SignData([System.Text.Encoding]::UTF8.GetBytes($meta), 'SHA256'))
$rsa.Dispose()
$obj = [ordered]@{
    format = "installed-kit-v1"
    published = $published
    expires = $expires
    version  = $ver
    url      = "Merno-$ver.bin"   # файл выпуска с этим номером (Updater.KitUri)
    sha256   = $sha
    size     = [long]$size
    sig_exe  = $sigExe
    sig_meta = $sigMeta
    notes    = $Notes
}
$json = $obj | ConvertTo-Json
$jsonFile = Join-Path $root 'dist\version.json'
[System.IO.File]::WriteAllText($jsonFile, $json, (New-Object System.Text.UTF8Encoding $false))
Write-Host "  Размер: $([math]::Round($size / 1MB, 1)) МБ, SHA-256: $sha"
Write-Host "  Описание: $Notes"
Write-Host '  dist\version.json записан и подписан.' -ForegroundColor Green

if ($NoUpload) { Write-Host '  Загрузка пропущена (-NoUpload).'; exit 0 }

# 6. выпуск на GitHub: сначала черновик со всеми файлами, затем публикация одним шагом —
#    до этого «последний выпуск» (откуда Merno берёт version.json) остаётся прежним.
# Windows PowerShell 5.1 при 'Stop' превращает любой вывод gh/git в stderr в прерывающую ошибку,
# поэтому дальше 'Continue', а успех проверяем по $LASTEXITCODE. Show — вывод команды человеку обычным текстом.
$ErrorActionPreference = 'Continue'
function Show { process { Write-Host "    $_" } }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail 'не найден gh. Установите: winget install GitHub.cli, затем gh auth login' }
& gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'gh не вошёл в GitHub: запустите gh auth login' }
# закрытый репозиторий: Merno без входа в GitHub не скачает обновление
$vis = & gh repo view $Repo --json visibility -q .visibility 2>$null
if ($LASTEXITCODE -ne 0) { Fail "не удалось открыть репозиторий $Repo (gh repo view)" }
if ("$vis".Trim() -ne 'PUBLIC') { Fail "репозиторий $Repo закрыт: Merno не сможет скачать обновление. Откройте его, потом выпускайте" }

# метка выпуска ставится ровно на этот коммит: его проверили и из него собрали файлы
if ((& git rev-parse --abbrev-ref HEAD) -ne 'main') { Fail 'выпуск делается из ветки main' }
if (& git status --porcelain --untracked-files=no) { Fail 'есть незакоммиченные изменения: закоммитьте их и запустите выпуск снова' }
$head = (& git rev-parse HEAD).Trim()
& git fetch -q origin main 2>&1 | Show
if ($LASTEXITCODE -ne 0) { Fail 'не удалось получить main с GitHub (git fetch)' }
if ($head -ne (& git rev-parse origin/main).Trim()) {
    & git merge-base --is-ancestor origin/main HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { Fail 'на GitHub в main есть коммиты, которых нет у вас: сначала git pull, потом выпускайте' }
    Write-Host '  Отправляю коммиты на GitHub (git push)...' -ForegroundColor Cyan
    & git push -q origin HEAD:main 2>&1 | Show
    if ($LASTEXITCODE -ne 0) { Fail 'не удалось отправить коммиты (git push)' }
}
& gh release view $ver --repo $Repo 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { Fail "выпуск $ver на GitHub уже есть" }
# метка без выпуска (выпуск удалили, метка осталась): gh взял бы её старый коммит
if (& git ls-remote --tags origin "refs/tags/$ver" 2>$null) {
    Fail "метка $ver на GitHub уже есть, а выпуска нет: удалите её (git push origin :refs/tags/$ver) и запустите выпуск снова"
}

$assets = Join-Path $root 'obj\release-assets'
if (Test-Path -LiteralPath $assets) { Remove-Item -LiteralPath $assets -Recurse -Force -ErrorAction Stop }
New-Item -ItemType Directory -Path $assets -ErrorAction Stop | Out-Null
$bin = Join-Path $assets "Merno-$ver.bin"
Copy-Item -LiteralPath $exe -Destination $bin -ErrorAction Stop
$notesFile = Join-Path $assets 'notes.md'
[System.IO.File]::WriteAllText($notesFile, $pageNotes, (New-Object System.Text.UTF8Encoding $false))
$portable = Join-Path $root 'dist\Merno-Portable.zip'
if (-not (Test-Path -LiteralPath $portable)) { Fail 'нет dist\Merno-Portable.zip' }
$files = @($bin, $jsonFile, $standalone, $portable)

Write-Host "  Создаю выпуск $ver на GitHub (черновик)..." -ForegroundColor Cyan
& gh release create $ver @files --repo $Repo --target $head --title "Merno $ver" --notes-file $notesFile --draft 2>&1 | Show
if ($LASTEXITCODE -ne 0) { Fail 'не удалось создать выпуск (gh release create)' }
& gh release edit $ver --repo $Repo --draft=false --latest 2>&1 | Show
if ($LASTEXITCODE -ne 0) { Fail "черновик $ver создан, но не опубликован: опубликуйте его на GitHub вручную" }

Write-Host ''
Write-Host "  Готово: версия $ver опубликована." -ForegroundColor Green
Write-Host "  Проверка: https://github.com/$Repo/releases/latest/download/version.json"
