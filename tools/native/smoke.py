# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
"""Safe native checks: no installation, UAC, Windows configuration changes, or network."""
import json
import os
from pathlib import Path
import struct
import subprocess

root = Path(__file__).resolve().parents[2] / "obj" / "installed-kit"
environment = dict(os.environ, DOTNET_STARTUP_HOOKS="untrusted.dll", CORECLR_ENABLE_PROFILING="1", COR_ENABLE_PROFILING="1", COMPLUS_ProfAPI_ProfilerCompatibilitySetting="EnableV2Profiler")
subprocess.run([str(root / "Merno.exe"), "--environment-self-test"], env=environment, check=True, timeout=15)
# Merno.Setup.exe (Inno) собирается позже, на этапе installer: здесь только то, что даёт этап binaries
for name in ("Merno.exe", "Merno.Setup.Test.exe"):
    data = (root / name).read_bytes()
    pe = struct.unpack_from("<I", data, 0x3c)[0]
    directories = 112 if struct.unpack_from("<H", data, pe + 24)[0] == 0x20B else 96   # PE32+ / PE32
    assert struct.unpack_from("<II", data, pe + 24 + directories + 14 * 8) == (0, 0), "Unexpected CLR header"
    assert struct.unpack_from("<H", data, pe + 24 + 68)[0] == 2, "Native entry point must use GUI subsystem"
subprocess.run([str(root / "Merno.Setup.Test.exe"), "--validate-package"], check=True, timeout=60)
target = root / "payload" / "Merno.exe"   # runtimeconfig встроен в exe (один файл)
original = target.read_bytes()
try:
    target.write_bytes(original + b"tampered")
    assert subprocess.run([str(root / "Merno.Setup.Test.exe"), "--validate-package"], timeout=60).returncode != 0
finally:
    target.write_bytes(original)
config = json.loads((root.parents[1] / "bin/Release/net10.0-windows/win-x64/Merno.runtimeconfig.json").read_bytes())
assert config["runtimeOptions"]["configProperties"]["System.StartupHookProvider.IsSupported"] is False
print("Native checks passed: pre-CLR environment, native PE, valid payload, tampered payload rejected")
