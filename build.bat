@echo off
rem SPDX-License-Identifier: GPL-3.0-or-later
rem Copyright © 2026 Merno contributors
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
