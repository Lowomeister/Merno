@echo off
rem SPDX-License-Identifier: GPL-3.0-or-later
rem Copyright © 2026 Merno contributors
rem Release in one go: version number + commit (tools\bump.ps1), build, checks and tests, sign, publish on GitHub (tools\release.ps1).
rem Signed release: put the files from the Merno-signed artifact into dist\ and run tools\release.ps1 -RequireSigned directly:
rem this file rebuilds dist\ and would replace them with unsigned ones.
cd /d "%~dp0"
rem Version number is set here, at release time: draft in CHANGELOG/whatsnew -> next number
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\bump.ps1"
if errorlevel 1 (
  echo   Version bump failed - release cancelled
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -NoPause
if errorlevel 1 (
  echo.
  echo   Build failed - release cancelled. The error is shown above.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\release.ps1" %*
echo.
pause
