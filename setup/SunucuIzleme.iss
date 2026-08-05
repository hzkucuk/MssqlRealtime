; Inno Setup script — Sunucu Izleme paneli
;
; Derlemek icin (Windows'ta, bir kez):
;   1. Inno Setup 6+ kurun:  https://jrsoftware.org/isdl.php
;   2. tools\windows-paketle.sh ile windows-publish klasorunu uretin
;   3. Bu dosyayi Inno Setup Compiler ile acip Build → Compile
;      (ya da: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup\SunucuIzleme.iss)
;
; Sonuc: setup\output\SunucuIzleme-Setup-<surum>.exe — musteriye verilecek tek dosya.
;
; Kurulum sirasinda operator hesabi ve genel adres sorulur; kullanici hicbir yapilandirma
; dosyasi duzenlemez. Parola makine seviyesinde ortam degiskenine yazilir (servis hesabi
; disinda okunamaz) ve ilk aciliste veritabanina hash'lenerek kaydedilir.

#define AppName "Sunucu Izleme"
#define AppVersion "0.8.0"
#define AppPublisher "hzkucuk"
#define ServiceName "SunucuIzleme"
#define ExeName "MssqlRealtime.Api.exe"
#define SourceDir "..\windows-publish"

[Setup]
AppId={{8F3C2A61-4D7E-4B93-9C15-6E2A7F8B1D40}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\SunucuIzleme
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=SunucuIzleme-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Servis kurulumu ve ProgramData yazimi icin yonetici gerekir.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#AppName}

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Veritabani ve veri koruma anahtarlari burada durur; kaldirmada SILINMEZ.
Name: "{commonappdata}\SunucuIzleme"; Permissions: service-full

[Icons]
Name: "{group}\Paneli Ac"; Filename: "http://127.0.0.1:{code:GetPort}/"
Name: "{group}\{#AppName} Kaldir"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#ExeName}"; Description: "Paneli tarayicida ac"; \
    Flags: postinstall shellexec skipifsilent runasoriginaluser; \
    Filename: "http://127.0.0.1:{code:GetPort}/"

[Code]
var
  ConfigPage: TInputQueryWizardPage;
  OriginPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ConfigPage := CreateInputQueryPage(wpSelectDir,
    'Yonetici hesabi',
    'Panele giris icin kullanacaginiz bilgiler',
    'Bu hesap ilk aciliste olusturulur. Parola sifirlama akisi YOKTUR — bir yere kaydedin.');
  ConfigPage.Add('E-posta (kullanici adi):', False);
  ConfigPage.Add('Parola (en az 10 karakter):', True);
  ConfigPage.Add('Parola (tekrar):', True);
  ConfigPage.Values[0] := 'admin@local';

  OriginPage := CreateInputQueryPage(ConfigPage.ID,
    'Ag ayarlari',
    'Panele nereden erisilecek?',
    'Genel adresi bos birakirsaniz panel yalnizca bu makineden (127.0.0.1) acilir.' + #13#10 +
    'Ters vekil sunucu (nginx / Nginx Proxy Manager) kullaniyorsaniz, telefonun bagladigi' + #13#10 +
    'adresi birebir yazin — sema dahil. Yanlis olursa giris CORS hatasi verir.');
  OriginPage.Add('Genel adres (or. https://izleme.firma.com):', False);
  OriginPage.Add('Port:', False);
  OriginPage.Values[1] := '5199';
end;

function GetPort(Param: string): string;
begin
  Result := OriginPage.Values[1];
  if Result = '' then Result := '5199';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Port: Integer;
begin
  Result := True;

  if CurPageID = ConfigPage.ID then
  begin
    if Length(ConfigPage.Values[1]) < 10 then
    begin
      MsgBox('Parola en az 10 karakter olmali.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if ConfigPage.Values[1] <> ConfigPage.Values[2] then
    begin
      MsgBox('Parolalar eslesmiyor.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if CurPageID = OriginPage.ID then
  begin
    Port := StrToIntDef(OriginPage.Values[1], 0);
    if (Port < 1) or (Port > 65535) then
    begin
      MsgBox('Port 1-65535 araliginda olmali.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    // Bir ters vekil sunucu arkasinda degilse duz HTTP'yi uyarmadan gecirme.
    if (OriginPage.Values[0] <> '') and (Pos('https://', LowerCase(OriginPage.Values[0])) <> 1) then
    begin
      if MsgBox('Genel adres https:// ile baslamiyor. Parolalar ve olcum verisi agda ACIK gider.' + #13#10 +
                'Yine de devam edilsin mi?', mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;

procedure SetMachineEnv(const Name, Value: string);
begin
  RegWriteStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment', Name, Value);
end;

procedure InstallService;
var
  ResultCode: Integer;
  Urls, Origin, DataDir: string;
begin
  DataDir := ExpandConstant('{commonappdata}\SunucuIzleme');
  Origin := OriginPage.Values[0];

  // Genel adres verilmediyse yalniz loopback: varsayilan kurulum kazara aga acilmasin.
  if Origin <> '' then
    Urls := 'http://0.0.0.0:' + GetPort('')
  else
    Urls := 'http://127.0.0.1:' + GetPort('');

  SetMachineEnv('ASPNETCORE_URLS', Urls);
  SetMachineEnv('ASPNETCORE_ENVIRONMENT', 'Production');
  SetMachineEnv('Storage__DataDirectory', DataDir);
  SetMachineEnv('Admin__Email', ConfigPage.Values[0]);
  SetMachineEnv('Admin__Password', ConfigPage.Values[1]);

  if Origin <> '' then
    SetMachineEnv('Cors__AllowedOrigins__0', Origin);

  // Ayni ad altinda eski bir servis varsa once temizle (yukseltme).
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);

  Exec(ExpandConstant('{sys}\sc.exe'),
    'create {#ServiceName} binPath= "' + ExpandConstant('{app}\{#ExeName}') + '" start= auto DisplayName= "{#AppName}"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec(ExpandConstant('{sys}\sc.exe'),
    'description {#ServiceName} "MSSQL ve site izleme paneli. Izlenen sunuculara yalnizca salt okunur baglanir."',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Cokerse yeniden baslat: sessizce duran bir izleme paneli, fark edilmeyen tek arizadir.
  Exec(ExpandConstant('{sys}\sc.exe'),
    'failure {#ServiceName} reset= 86400 actions= restart/5000/restart/15000/restart/60000',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Guvenlik duvari yalnizca disaridan erisim gerekiyorsa acilir.
  if Origin <> '' then
    Exec(ExpandConstant('{sys}\netsh.exe'),
      'advfirewall firewall add rule name="{#AppName} (' + GetPort('') + ')" dir=in action=allow protocol=TCP localport=' + GetPort('') + ' profile=domain,private',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallService;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Exec(ExpandConstant('{sys}\netsh.exe'),
      'advfirewall firewall delete rule name="{#AppName} (' + GetPort('') + ')"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    // Veri klasoru bilerek birakilir: icinde izlenen sunucu profilleri ve veri koruma
    // anahtarlari var. Anahtarlar silinirse kayitli SQL parolalari geri getirilemez.
    MsgBox('Kaldirma tamamlandi.' + #13#10 + #13#10 +
           'Veriler korundu: ' + ExpandConstant('{commonappdata}\SunucuIzleme') + #13#10 +
           'Tamamen silmek isterseniz bu klasoru elle kaldirin.', mbInformation, MB_OK);
  end;
end;
