# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright © 2026 Merno contributors
"""Build an installed kit. Uses a local verified Zig compiler; never publishes or signs a release."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "obj" / "installed-kit"
PAYLOAD = OUT / "payload"
# ровно та версия, которую скачивает и проверяет get-zig.py
ZIG = next((ROOT / "obj" / "native-tools").glob("zig-*-windows-0.15.2/zig.exe"), None)
VERSION = re.search(r'const string Version = "([\d.]+)"', (ROOT / "App.xaml.cs").read_text(encoding="utf-8-sig")).group(1)
# Этапы (для подписи SignPath между ними, см. .github/workflows/build.yml):
#   binaries  — программа (obj/installed-kit/payload), запускатель (obj/installed-kit/Merno.exe), портативный exe (obj/portable)
#   installer — установщик Inno, комплект обновления и архив портативной из уже (подписанных) файлов
#   без аргумента — оба подряд (локальная сборка)
STAGE = sys.argv[1] if len(sys.argv) > 1 else "all"
if STAGE not in ("all", "binaries", "installer"):
    raise SystemExit("Этап: binaries, installer или без аргумента")
PORTABLE = ROOT / "obj" / "portable"
licenses = PAYLOAD / "licenses"
if STAGE in ("all", "binaries"):
    if not ZIG:
        raise SystemExit("Нет компилятора Zig 0.15.2 в obj/native-tools: сначала запустите python tools/native/get-zig.py")
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.rmtree(PAYLOAD, ignore_errors=True)   # без остатков прежней раскладки (отдельных DLL)
    subprocess.run(["dotnet", "publish", str(ROOT / "Merno.csproj"), "-c", "Release", "--no-restore", "-o", str(PAYLOAD),
                    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=false", "-p:UseSharedCompilation=false"], check=True)
    # Сборки .NET (в том числе Merno.dll) — внутри Merno.exe, а не отдельными файлами: «Интеллектуальное управление
    # приложениями» Windows 11 блокирует неподписанные DLL. Нативные библиотеки (подписаны Microsoft) лежат рядом и
    # никуда не распаковываются.
    licenses.mkdir(exist_ok=True)
    for source, name in [(ROOT / "LICENSE.txt", "Merno-LICENSE.txt"),
                         (ROOT / "Assets/Fonts/OFL.txt", "Onest-OFL.txt"),
                         (ZIG.parent / "LICENSE", "Zig-LICENSE.txt"),
                         (ZIG.parent / "lib/libc/mingw/COPYING", "MinGW-COPYING.txt"),
                         (ROOT / "THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.md")]:
        shutil.copyfile(source, licenses / name)
    assets = json.loads((ROOT / "obj/project.assets.json").read_text(encoding="utf-8-sig"))
    for framework in assets["project"]["frameworks"].values():
        for dependency in framework.get("downloadDependencies", []):
            if dependency["name"].startswith(("Microsoft.NETCore.App.Runtime.", "Microsoft.WindowsDesktop.App.Runtime.")):
                version = dependency["version"].strip("[]").split(",")[0].strip()
                assets["libraries"][dependency["name"] + "/" + version] = {
                    "type": "package", "path": dependency["name"].lower() + "/" + version}
    def package_dir(details):
        return next((Path(f) / details["path"] for f in assets["packageFolders"] if (Path(f) / details["path"]).is_dir()), None)
    def source_commit(folder):
        # репозиторий и коммит, из которых собран пакет (из .nuspec)
        spec = next(folder.glob("*.nuspec")).read_text(encoding="utf-8-sig")
        found = re.search(r'<repository type="git" url="([^"]+)" commit="([0-9a-f]{40})"', spec)
        return (found.groups() if found else None), spec
    runtime_key = next(k for k in assets["libraries"] if k.startswith("Microsoft.NETCore.App.Runtime."))
    runtime_dir = package_dir(assets["libraries"][runtime_key])
    components = []
    for library, details in assets["libraries"].items():
        if details.get("type") != "package":
            continue
        copied = []
        package = package_dir(details)
        if package:
            for source in package.iterdir():
                if source.is_file() and re.fullmatch(r"(?:LICENSE|THIRD-PARTY-NOTICES)(?:\.[A-Za-z]+)?", source.name, re.IGNORECASE):
                    name = library.replace("/", "-") + "-" + source.name
                    shutil.copyfile(source, licenses / name)
                    copied.append(name)
        if package and not any(re.search(r"-LICENSE(?:\.|$)", name, re.IGNORECASE) for name in copied):
            # пакеты .NET 10 (например, System.Management) указывают MIT только строкой в .nuspec, без файла.
            # Если пакет собран из того же коммита dotnet/dotnet, что и runtime, берём текст MIT из runtime.
            origin, spec = source_commit(package)
            if '<license type="expression">MIT</license>' in spec and origin and origin == source_commit(runtime_dir)[0]:
                name = library.replace("/", "-") + "-LICENSE.TXT"
                shutil.copyfile(runtime_dir / "LICENSE.TXT", licenses / name)
                copied.append(name)
        if not any(re.search(r"-LICENSE(?:\.|$)", name, re.IGNORECASE) for name in copied):
            raise SystemExit("Нет файла лицензии у пакета: " + library)
        components.append({"component": library, "notices": sorted(copied)})
    desktop_versions = {key.split("/", 1)[1] for key in assets["libraries"] if key.startswith("Microsoft.WindowsDesktop.App.Runtime.")}
    upstream = json.loads((ROOT / "docs/licenses/upstream.json").read_text(encoding="utf-8"))
    if not desktop_versions or any(item["runtime_version"] not in desktop_versions for item in upstream) or len(upstream) != 2:
        raise SystemExit("Версия WindowsDesktop runtime не совпадает с docs/licenses/upstream.json: "
                         "поставьте SDK из global.json или обновите уведомления WPF/Windows Forms.")
    for item in upstream:
        source = ROOT / "docs/licenses" / item["file"]
        if hashlib.sha256(source.read_bytes()).hexdigest() != item["sha256"]:
            raise SystemExit("Файл уведомлений изменён (не совпадает SHA-256): " + item["file"])
        shutil.copyfile(source, licenses / source.name)
    components.append({"component": "WindowsDesktop upstream notices", "sources": upstream})
    (licenses / "COMPONENTS.json").write_text(json.dumps(components, indent=2) + "\n", encoding="utf-8")
    SOURCE = ROOT / "tools" / "native" / "host.c"
    def compile_native(output, *extra):
        environment = dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(ROOT / "obj" / "zig-cache"), ZIG_LOCAL_CACHE_DIR=str(ROOT / "obj" / "zig-local-cache"))
        subprocess.run([str(ZIG), "cc", "-target", "x86_64-windows-gnu", "-O2", "-municode", "-Wl,--subsystem,windows", "-static", str(SOURCE),
                        "-I", str(OUT), "-o", str(output), "-ladvapi32", "-lshell32", "-lole32", "-luser32", *extra], check=True, env=environment)
    (OUT / "launcher.rc").write_text('1 ICON "' + (ROOT / "Assets/app.ico").as_posix() + '"\n', encoding="utf-8")
    subprocess.run([str(ZIG), "rc", "/fo", str(OUT / "launcher.res"), str(OUT / "launcher.rc")], check=True,
                   env=dict(os.environ, ZIG_GLOBAL_CACHE_DIR=str(ROOT / "obj" / "zig-cache"), ZIG_LOCAL_CACHE_DIR=str(ROOT / "obj" / "zig-local-cache")))
    compile_native(OUT / "Merno.exe", str(OUT / "launcher.res"))
    records = []
    for file in sorted(PAYLOAD.rglob("*")):
        if not file.is_file():
            continue
        name = file.relative_to(PAYLOAD).as_posix()
        if not re.fullmatch(r"[A-Za-z0-9_. /-]+", name) or any(p in (".", "..") for p in name.split("/")):
            raise SystemExit("Недопустимое имя файла в комплекте: " + name)
        digest = hashlib.sha256(file.read_bytes()).digest()
        escaped = name.replace("/", "\\\\")
        records.append('{L"' + escaped + '",{' + ",".join(str(x) for x in digest) + "}}")
    (OUT / "payload.h").write_text(
        'static const struct {const wchar_t *path;unsigned char hash[32];} payload_files[]={\n' + ",\n".join(records) +
        '\n};\n#define PAYLOAD_COUNT (sizeof payload_files/sizeof payload_files[0])\n', encoding="ascii")
    # проверочная сборка для smoke.py: сверяет файлы комплекта с хешами (в выпуск не входит)
    compile_native(OUT / "Merno.Setup.Test.exe", "-DPACKAGE_CHECK")
    # портативная версия: один exe, без установки (без автозапуска и обновления по воздуху)
    shutil.rmtree(PORTABLE, ignore_errors=True)
    subprocess.run(["dotnet", "publish", str(ROOT / "Merno.csproj"), "-c", "Release", "--no-restore", "-o", str(PORTABLE),
                    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
                    # у портативной нет запускателя: права администратора просит сам exe (у установленной — asInvoker, просит запускатель)
                    "-p:ApplicationManifest=" + str(ROOT / "tools" / "installer" / "portable.manifest"),
                    "-p:UseSharedCompilation=false"], check=True)
if STAGE in ("all", "installer"):
    # Полноценный установщик — Inno Setup (tools/installer/Merno.iss). Имя Merno.Setup.exe внутри комплекта
    # обновления не менять: Core/Updater.cs (ExtractKit) ищет именно его и запускает с --wait-pid.
    (OUT / "current.txt").write_bytes(VERSION.encode("ascii"))
    ISCC = next((p for p in (Path(os.environ.get("LOCALAPPDATA", "")) / "Programs/Inno Setup 6/ISCC.exe",
                             Path(os.environ.get("ProgramFiles(x86)", "")) / "Inno Setup 6/ISCC.exe",
                             Path(os.environ.get("ProgramFiles", "")) / "Inno Setup 6/ISCC.exe") if p.is_file()), None)
    if not ISCC:
        raise SystemExit("Inno Setup 6 не найден. Установите: winget install JRSoftware.InnoSetup")
    (OUT / "Merno.Setup.exe").unlink(missing_ok=True)
    subprocess.run([str(ISCC), "/Q", "/DMernoVer=" + VERSION, "/DKit=" + str(OUT), "/DRoot=" + str(ROOT), "/O" + str(OUT),
                    str(ROOT / "tools" / "installer" / "Merno.iss")], check=True)
    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)
    shutil.copyfile(OUT / "Merno.Setup.exe", dist / "Merno-Setup.exe")
    # комплект для обновления по воздуху: подписывается tools/release.ps1
    distribution = dist / "Merno-Setup.zip"
    with zipfile.ZipFile(distribution, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(OUT / "Merno.Setup.exe", "Merno.Setup.exe")
    # с уведомлениями о лицензиях, как и в установщике
    with zipfile.ZipFile(dist / "Merno-Portable.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(PORTABLE / "Merno.exe", "Merno/Merno.exe")
        for file in licenses.rglob("*"):
            if file.is_file():
                archive.write(file, "Merno/licenses/" + file.relative_to(licenses).as_posix())
    print("Installer:", dist / "Merno-Setup.exe", "portable:", dist / "Merno-Portable.zip", "update kit:", distribution, "version:", VERSION)
