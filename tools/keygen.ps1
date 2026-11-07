# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
# Создание ключа подписи обновлений Merno (один раз).
# Закрытый ключ -> %USERPROFILE%\.merno\update-private.xml (НИКОГДА не в папку проекта).
# Открытый ключ -> Core\UpdateKey.cs (встраивается в программу).
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File tools\keygen.ps1 [-Force]
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$keyDir  = Join-Path $env:USERPROFILE '.merno'
$keyFile = Join-Path $keyDir 'update-private.xml'
$csFile  = Join-Path $root 'Core\UpdateKey.cs'

if ((Test-Path -LiteralPath $keyFile) -and -not $Force) {
    Write-Host "  Закрытый ключ уже есть: $keyFile" -ForegroundColor Yellow
    Write-Host '  Новый ключ создаётся только с параметром -Force.' -ForegroundColor Yellow
    Write-Host '  ВНИМАНИЕ: после смены ключа уже установленные копии Merno НЕ примут обновления, подписанные новым ключом.' -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path -LiteralPath $keyDir)) { New-Item -ItemType Directory -Path $keyDir | Out-Null }

$csp = New-Object System.Security.Cryptography.CspParameters 24   # PROV_RSA_AES — поддерживает SHA-256
$csp.Flags = [System.Security.Cryptography.CspProviderFlags]::CreateEphemeralKey
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 3072, $csp
$rsa.PersistKeyInCsp = $false
$private = $rsa.ToXmlString($true)
$public  = $rsa.ToXmlString($false)
$rsa.Dispose()

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($keyFile, $private, $utf8)
# доступ к файлу — только текущему пользователю
try {
    $acl = New-Object System.Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($me, 'FullControl', 'Allow')))
    Set-Acl -LiteralPath $keyFile -AclObject $acl
} catch { Write-Host "  (не удалось ограничить права на файл ключа: $($_.Exception.Message))" -ForegroundColor DarkYellow }

$escaped = $public.Replace('\', '\\').Replace('"', '\"')
$cs = @"
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

namespace Merno.Core;

/// <summary>
/// Открытый ключ RSA-3072 для проверки подписи обновлений (создаётся tools\keygen.ps1).
/// Закрытый ключ в репозиторий не входит.
/// </summary>
public static class UpdateKey
{
    public const string PublicXml = "$escaped";
}
"@
[System.IO.File]::WriteAllText($csFile, $cs, $utf8)

Write-Host ''
Write-Host '  Готово.' -ForegroundColor Green
Write-Host "  Закрытый ключ: $keyFile"
Write-Host "  Открытый ключ записан в: $csFile"
Write-Host ''
Write-Host '  ОБЯЗАТЕЛЬНО сделайте резервную копию update-private.xml (флешка, менеджер паролей).' -ForegroundColor Yellow
Write-Host '  Если потеряете ключ, уже установленные копии Merno больше не смогут обновиться автоматически.' -ForegroundColor Yellow
Write-Host '  Никому не передавайте этот файл и не кладите его в папку проекта или на GitHub.' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Дальше: соберите программу (build.bat), затем выпустите её: tools\release.ps1' -ForegroundColor Cyan
