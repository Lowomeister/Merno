; SPDX-License-Identifier: GPL-3.0-or-later
; Copyright © 2026 Merno contributors
; Установщик Merno (Inno Setup 6). Собирает tools/native/package.py:
;   ISCC.exe /DMernoVer=1.0.0/DKit=<obj\installed-kit> /DRoot=<папка проекта> /O<куда> tools\installer\Merno.iss
; Раскладка (её проверяет запускатель tools/native/host.c, папку менять нельзя):
;   C:\Program Files\Merno\Merno.exe            запускатель (со значком Merno)
;   C:\Program Files\Merno\current.txt          выбранная версия
;   C:\Program Files\Merno\versions\<версия>\   файлы программы; прежние версии удаляются после успешной установки
; Обновление по воздуху запускает этот же установщик с /SILENT и --wait-pid <PID> (Core/Updater.cs, Apply).
; Не встало, тогда установщик говорит об этом и снова открывает прежнюю версию (ReturnOldMerno).

#ifndef MernoVer
  #error Нужен /DMernoVer=<версия>
#endif

[Setup]
AppId={{7E0B3C5A-4F7D-4C1E-9A39-5B2D6E8F1A40}
AppName=Merno
AppVersion={#MernoVer}
AppVerName=Merno {#MernoVer}
AppPublisher=Merno contributors
AppCopyright=Copyright © 2026 Merno contributors
VersionInfoVersion={#MernoVer}
VersionInfoDescription=Установка Merno
DefaultDirName={autopf}\Merno
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile={#Root}\Assets\app.ico
UninstallDisplayIcon={app}\Merno.exe,0
UninstallDisplayName=Merno
; GPL не требует согласия для использования, поэтому не «Принять лицензию», а короткая справка по-русски.
; Полный текст GPL ставится в licenses и открывается в Merno («Настройки» → «Текст лицензии»).
InfoBeforeFile={#Root}\tools\installer\info-ru.txt
OutputBaseFilename=Merno.Setup
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
; стандартные заголовки Inno с тире; у нас без тире и без слова «деинсталляция»
ru.SetupWindowTitle=Установка %1
ru.UninstallAppFullTitle=Удаление %1

[CustomMessages]
ru.DesktopIcon=Ярлык на рабочем столе
ru.LaunchMerno=Запустить Merno
ru.MernoRunning=Merno сейчас открыт. Закройте его (в том числе значок в трее) и нажмите «Повтор».
ru.UpdateWaiting=Обновление ждёт, пока закроется Merno. Если Merno открыт, закройте его (в том числе значок в трее) и нажмите «Повтор». Если нажать «Отмена», останется прежняя версия.
ru.UpdateFailed=Обновление Merno не установилось. Прежняя версия осталась, обновить можно позже.

[Tasks]
; при обновлении по воздуху ярлык на рабочем столе не появляется сам (кто ставил его раньше, у того он остаётся)
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Check: NotAutoUpdate

[InstallDelete]
; прежние запускатели после замены (занятый останется до следующего раза)
Type: files; Name: "{app}\Merno.exe.old*"

[Files]
; порядок важен: сначала программа, потом запускатель, current.txt последним.
; Прервётся установка, и current.txt останется указывать на прежнюю целую версию.
Source: "{#Kit}\payload\*"; DestDir: "{app}\versions\{#MernoVer}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Kit}\Merno.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Kit}\current.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Merno"; Filename: "{app}\Merno.exe"
Name: "{autodesktop}\Merno"; Filename: "{app}\Merno.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Merno.exe"; Description: "{cm:LaunchMerno}"; Flags: postinstall nowait skipifsilent; Check: NotAutoUpdate
; после обновления по воздуху (и тихой установки) Merno открывается сам
Filename: "{app}\Merno.exe"; Flags: nowait; Check: OpenWithoutAsking

[UninstallRun]
; автозапуск Merno (задача планировщика, Core/Autostart.cs)
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN Merno /F"; Flags: runhidden; RunOnceId: "MernoAutostart"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  MernoMutex = 'Local\Merno.SingleInstance';

var
  Launcher, LauncherOld: String;
  LauncherMoved: Boolean;
  { InstallDone: файлы новой версии на месте (ssPostInstall). Relaunched: прежнюю Merno уже открывали. }
  InstallDone, Relaunched: Boolean;

function MessageBoxW(hWnd: Integer; lpText, lpCaption: String; uType: Cardinal): Integer;
  external 'MessageBoxW@user32.dll stdcall';

{ Окно поверх всех: установщик обновления запускается в фоне и ждёт до минуты, за это время Windows
  перестаёт пускать его окна вперёд, и обычный MsgBox остаётся за другими окнами.
  MB_TOPMOST $40000, MB_SETFOREGROUND $10000, MB_ICONWARNING $30. /SUPPRESSMSGBOXES это окно не глушит. }
function TopMsg(const Text: String; Buttons: Cardinal): Integer;
begin
  Log('Message box (top): ' + Text);
  Result := MessageBoxW(0, Text, 'Merno', Buttons or $30 or $10000 or $40000);
  Log('Message box result: ' + IntToStr(Result));
end;

function HasParam(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then Result := True;
end;

{ Обновление по воздуху: Merno запускает установщик с --wait-pid <PID> (Core/Updater.cs, Apply). }
function IsAutoUpdate(): Boolean;
begin
  Result := HasParam('--wait-pid');
end;

function NotAutoUpdate(): Boolean;
begin
  Result := not IsAutoUpdate();
end;

function OpenWithoutAsking(): Boolean;
begin
  Result := WizardSilent() or IsAutoUpdate();
end;

{ PID прежней копии из --wait-pid <PID>; 0, если не задан. }
function WaitPid(): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 1 to ParamCount - 1 do
    if CompareText(ParamStr(I), '--wait-pid') = 0 then Result := StrToIntDef(ParamStr(I + 1), 0);
end;

{ Ждём, пока Merno отпустит мьютекс, не дольше Tries раз по 0,5 с. True: Merno закрылся. }
function MernoClosedWithin(Tries: Integer): Boolean;
var
  I: Integer;
begin
  I := 0;
  while CheckForMutexes(MernoMutex) and (I < Tries) do
  begin
    Sleep(500);
    I := I + 1;
  end;
  Result := not CheckForMutexes(MernoMutex);
end;

{ Прежняя копия так и не закрылась после «Обновить». Окна и значка в трее у неё уже нет, сам человек её
  не закроет. Закрываем именно её: тот же PID и имя Merno.exe (так же Program.cs поступает при --updated). }
procedure CloseStuckMerno(Pid: Integer);
var
  Code: Integer;
begin
  if Pid <= 0 then exit;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /FI "PID eq ' + IntToStr(Pid) + '" /FI "IMAGENAME eq Merno.exe"',
       ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, Code);
end;

{ Ждём, пока Merno закроется. Тихая установка без --wait-pid: без вопросов, не закрылся, значит отказ.
  Обновление по воздуху: ждём минуту, подвисшую прежнюю копию закрываем сами, а если Merno всё равно
  открыт, спрашиваем и в тихом режиме (TopMsg не глушится /SUPPRESSMSGBOXES). Иначе обновление
  срывалось молча, и человек оставался без Merno. }
function WaitMernoClosed(Silent: Boolean): Boolean;
var
  Question: String;
begin
  Question := CustomMessage('MernoRunning');
  if IsAutoUpdate() then
  begin
    Question := CustomMessage('UpdateWaiting');
    if not MernoClosedWithin(120) then
    begin
      CloseStuckMerno(WaitPid());
      MernoClosedWithin(20);
    end;
    { запускатель выходит сразу после программы: даём ему эти миллисекунды }
    Sleep(500);
  end;
  while CheckForMutexes(MernoMutex) do
  begin
    if Silent and not IsAutoUpdate() then begin Result := False; exit; end;
    if TopMsg(Question, MB_RETRYCANCEL) = IDCANCEL then begin Result := False; exit; end;
  end;
  Result := True;
end;

{ Обновление по воздуху не встало, а прежняя Merno уже закрылась: открываем её снова, иначе человек
  останется без программы. Путь как у запускателя (tools/native/host.c): 64-битный Program Files\Merno.
  Константу app здесь брать нельзя: если InitializeSetup вернул False, она ещё не задана. }
procedure ReturnOldMerno(Explain: Boolean);
var
  Exe: String;
  Code: Integer;
begin
  if Relaunched then exit;
  Relaunched := True;
  if Explain then TopMsg(CustomMessage('UpdateFailed'), MB_OK);
  if CheckForMutexes(MernoMutex) then exit;   { Merno уже открыт: второй запуск только покажет его окно }
  Exe := '';
  try
    Exe := ExpandConstant('{commonpf64}\Merno\Merno.exe');
  except
    Exe := '';
  end;
  if (Exe <> '') and FileExists(Exe) then Exec(Exe, '', ExtractFileDir(Exe), SW_SHOWNORMAL, ewNoWait, Code);
end;

{ Прежние версии удаляем только после успешной установки новой. }
procedure RemoveOldVersions();
var
  FindRec: TFindRec;
  Dir: String;
begin
  Dir := ExpandConstant('{app}\versions');
  if FindFirst(Dir + '\*', FindRec) then
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and (FindRec.Name <> '.') and (FindRec.Name <> '..') and
         (CompareText(FindRec.Name, '{#MernoVer}') <> 0) then
        DelTree(Dir + '\' + FindRec.Name, True, True, True);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    { Работающий exe нельзя перезаписать, но можно переименовать. Так замена запускателя не упрётся
      в копию, которая ещё закрывается. }
    Launcher := ExpandConstant('{app}\Merno.exe');
    LauncherOld := Launcher + '.old';
    LauncherMoved := False;
    if FileExists(Launcher) then
    begin
      { прежний .old ещё занят (две установки подряд): берём другое имя }
      if FileExists(LauncherOld) and not DeleteFile(LauncherOld) then
        LauncherOld := Launcher + '.old-' + GetDateTimeString('yyyymmddhhnnss', #0, #0);
      LauncherMoved := RenameFile(Launcher, LauncherOld);
    end;
  end
  else if CurStep = ssPostInstall then
  begin
    InstallDone := True;
    RemoveOldVersions();
    if LauncherMoved then DeleteFile(LauncherOld);
    LauncherMoved := False;
  end;
end;

{ Если откат установки удалил current.txt, а версия на диске одна, указываем на неё:
  иначе запускатель не найдёт установленную версию. }
procedure RestoreCurrentVersion();
var
  FindRec: TFindRec;
  Current, Found: String;
  Count: Integer;
begin
  Current := ExpandConstant('{app}\current.txt');
  if FileExists(Current) or not DirExists(ExpandConstant('{app}\versions')) then exit;
  Count := 0;
  if FindFirst(ExpandConstant('{app}\versions\*'), FindRec) then
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Found := FindRec.Name;
        Count := Count + 1;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
  if Count = 1 then SaveStringToFile(Current, Found, False);
end;

procedure DeinitializeSetup();
begin
  if Launcher <> '' then   { дело дошло до установки }
  begin
    { установка прервалась после переименования: возвращаем прежний запускатель }
    if LauncherMoved and not FileExists(Launcher) and FileExists(LauncherOld) then
      RenameFile(LauncherOld, Launcher);
    RestoreCurrentVersion();
  end;
  { обновление по воздуху прервалось (отмена, ошибка, скрытая /SUPPRESSMSGBOXES): прежняя Merno уже закрыта.
    Говорим об этом и открываем её, когда запускатель и current.txt уже возвращены. }
  if IsAutoUpdate() and not InstallDone then ReturnOldMerno(True);
end;

function InitializeSetup(): Boolean;
begin
  Result := WaitMernoClosed(WizardSilent);
  { человек нажал «Отмена»: прежняя версия остаётся, открываем её сразу, не полагаясь на DeinitializeSetup }
  if not Result and IsAutoUpdate() then ReturnOldMerno(False);
end;

function InitializeUninstall(): Boolean;
begin
  Result := WaitMernoClosed(UninstallSilent);
end;
