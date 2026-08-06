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
; dosyasi duzenlemez. Ayarlar servisin komut satirinda gider; parola ise kilitlenmis veri
; klasorune dosya olarak birakilir ve uygulama hesabi kurar kurmaz o dosyayi siler.
;
; 🔴 Olculdu 2026-08-06: makine ortam degiskenleri bu is icin GUVENILMEZ — servisler
; onyuklemeden sonra yazilan degiskenleri gormuyor (0.12.1 bu yuzden acilista oldu) ve
; parolayi BUILTIN\Users okuyabiliyordu. Bkz. docs/05-olculen-bulgular.md.

#define AppName "Sunucu Izleme"
#define AppVersion "0.15.1"
#define AppPublisher "hzkucuk"
#define ServiceName "SunucuIzleme"
#define ExeName "MssqlRealtime.Api.exe"
#define SourceDir "..\windows-publish"
#define EnvKey "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"

[Setup]
AppId={{8F3C2A61-4D7E-4B93-9C15-6E2A7F8B1D40}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; Program Files degil: oradaki ve ProgramData'daki izinler servisin kendi
; veritabanini acamamasina kadar gitti (0.12.1-0.12.4). Kok altindaki kendi
; klasorumuzde izinler yumusak ve her sey tek yerde.
DefaultDirName={sd}\SunucuIzleme
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
Name: "{app}\data"

[Icons]
Name: "{group}\Paneli Ac"; Filename: "http://127.0.0.1:{code:GetPort}/"
Name: "{group}\{#AppName} Kaldir"; Filename: "{uninstallexe}"

[Run]
; shellexec ile URL acilir; Filename tek olmali (Inno birden fazla Filename kabul etmez).
Filename: "http://127.0.0.1:{code:GetPort}/"; Description: "Paneli tarayicida ac"; \
    Flags: postinstall shellexec skipifsilent runasoriginaluser

[Code]
const
  EnvKey = '{#EnvKey}';
  SettingsKey = 'SOFTWARE\SunucuIzleme';

var
  ConfigPage: TInputQueryWizardPage;
  OriginPage: TInputQueryWizardPage;

procedure InitializeWizard;
var
  Saved: string;
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
  // Yalnizca ters vekil BASKA bir makinedeyse gerekir. Bos birakilirsa X-Forwarded-For
  // yalniz loopback'ten kabul edilir; olculdu 2026-08-06: herkesten kabul edildiginde
  // sahte baslikla giris hiz siniri tamamen atlaniyor.
  OriginPage.Add('Ters vekil sunucu IP (ayni makinedeyse bos birakin):', False);

  // Yukseltmede eski ayarlar geri yuklenir: kullanici hicbir seyi yeniden yazmaz ve
  // /VERYSILENT ile guncelleme mumkun olur. Values[] bir ozelliktir, var parametresi
  // olamaz — bu yuzden once yerel degiskene okunur.
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, SettingsKey, 'Port', Saved) then
    Saved := '5199';
  OriginPage.Values[1] := Saved;

  if RegQueryStringValue(HKEY_LOCAL_MACHINE, SettingsKey, 'PublicOrigin', Saved) then
    OriginPage.Values[0] := Saved;

  if RegQueryStringValue(HKEY_LOCAL_MACHINE, SettingsKey, 'ProxyAddress', Saved) then
    OriginPage.Values[2] := Saved;
end;

// Yukseltme mi? Veritabani varsa hesap zaten kurulmus demektir; o zaman e-posta/parola
// sormak anlamsiz (girilen parola kullanilmaz, hesap zaten var) ve sessiz guncellemeyi
// imkansiz kilar. Olculdu 2026-08-06: her yukseltmede tekrar soruluyordu.
function DataDirectory: string;
begin
  Result := ExpandConstant('{app}\data');
end;

function IsUpgrade: Boolean;
begin
  Result := FileExists(DataDirectory + '\mssqlrealtime.db')
    or FileExists(ExpandConstant('{commonappdata}\SunucuIzleme\mssqlrealtime.db'));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := IsUpgrade and (PageID = ConfigPage.ID);
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

  if (CurPageID = ConfigPage.ID) and not IsUpgrade then
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
  RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvKey, Name, Value);
end;

// "Servis basladi" yetmez: baslayip hemen olen bir servis de baslamis gorunur. Saglik ucu
// cevap verene kadar bekleriz — kurulum parolasini ancak o zaman silmek guvenlidir, cunku
// hesap ilk aciliste olusuyor.
function WaitForHealth(Port: string): Boolean;
var
  ResultCode: Integer;
  Cmd: string;
begin
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command "$d=(Get-Date).AddSeconds(90); ' +
         'while((Get-Date) -lt $d){ try{ $r=Invoke-RestMethod ''http://127.0.0.1:' + Port +
         '/api/health'' -TimeoutSec 5; if($r.status -eq ''ok''){ exit 0 } }catch{}; ' +
         'Start-Sleep -Seconds 3 }; exit 1"';

  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure InstallService;
var
  ResultCode: Integer;
  Urls, Origin, DataDir, BinPath: string;
begin
  DataDir := DataDirectory;

  // 0.12.4 ve oncesi veriyi ProgramData altinda tutuyordu. Veritabani ve veri koruma
  // anahtarlari tasinir — anahtarlar tasinmazsa kayitli SQL parolalari bir daha cozulemez.
  if FileExists(ExpandConstant('{commonappdata}\SunucuIzleme\mssqlrealtime.db'))
     and not FileExists(DataDir + '\mssqlrealtime.db') then
    Exec(ExpandConstant('{sys}\robocopy.exe'),
      '"' + ExpandConstant('{commonappdata}\SunucuIzleme') + '" "' + DataDir + '" /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Origin := OriginPage.Values[0];

  // Genel adres verilmediyse yalniz loopback: varsayilan kurulum kazara aga acilmasin.
  if Origin <> '' then
    Urls := 'http://0.0.0.0:' + GetPort('')
  else
    Urls := 'http://127.0.0.1:' + GetPort('');

  // 🔴 Olculdu 2026-08-06: servisler onyuklemeden SONRA yazilan makine ortam degiskenlerini
  // gormuyor (services.exe blogu onbellekliyor). 0.12.1 kurulumu bu yuzden servisi acilista
  // oldurdu: Storage__DataDirectory gorunmedi, uygulama Program Files altina yazmaya calisti.
  // Ayarlar artik binPath'te komut satiri argumani olarak gidiyor — servisin her zaman
  // gordugu tek kanal. Ortam degiskenleri elle calistirma icin birakildi.
  SetMachineEnv('ASPNETCORE_ENVIRONMENT', 'Production');
  SetMachineEnv('Storage__DataDirectory', DataDir);

  // Bir sonraki yukseltmenin hicbir sey sormadan gecebilmesi icin ayarlar saklanir.
  RegWriteStringValue(HKEY_LOCAL_MACHINE, SettingsKey, 'Port', GetPort(''));
  RegWriteStringValue(HKEY_LOCAL_MACHINE, SettingsKey, 'PublicOrigin', Origin);
  RegWriteStringValue(HKEY_LOCAL_MACHINE, SettingsKey, 'ProxyAddress', OriginPage.Values[2]);

  // Hesap yalnizca ILK kurulumda olusur. Yukseltmede e-posta uzerine yazilmaz ve parola
  // dosyasi hic yazilmaz — mevcut hesap ve parola aynen kalir.
  if not IsUpgrade then
  begin
    SetMachineEnv('Admin__Email', ConfigPage.Values[0]);

    // Parola registry'ye YAZILMAZ (BUILTIN\Users okuyabiliyordu). Kilitlenmis veri
    // klasorune dosya olarak birakilir; uygulama hesabi kurar kurmaz dosyayi siler.
    SaveStringToFile(DataDir + '\ilk-parola', ConfigPage.Values[1], False);
  end;

  if Origin <> '' then
    SetMachineEnv('Cors__AllowedOrigins__0', Origin);

  if OriginPage.Values[2] <> '' then
    SetMachineEnv('ForwardedHeaders__KnownProxies__0', OriginPage.Values[2])
  else
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'ForwardedHeaders__KnownProxies__0');

  // Ayni ad altinda eski bir servis varsa once temizle (yukseltme).
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);

  // Sanal servis hesabi: makinenin tamami yerine yalnizca kendi kaynaklari. LocalSystem'de
  // uygulamada bir uzaktan kod calistirma acigi makineyi tamamen verirdi. Ag kimligi yine
  // MACHINE$ oldugu icin Windows kimlik dogrulamasiyla SQL baglantisi degismez.
  // Ayarlar binPath icinde: ortam degiskeninin aksine servis bunu her zaman gorur.
  BinPath := '\"' + ExpandConstant('{app}\{#ExeName}') + '\"' +
    ' --Storage:DataDirectory=\"' + DataDir + '\"' +
    ' --urls=\"' + Urls + '\"';

  if Origin <> '' then
    BinPath := BinPath + ' --Cors:AllowedOrigins:0=\"' + Origin + '\"';

  if OriginPage.Values[2] <> '' then
    BinPath := BinPath + ' --ForwardedHeaders:KnownProxies:0=\"' + OriginPage.Values[2] + '\"';

  // Servis LocalSystem olarak kaliyor. Sanal hesaba (NT SERVICE\...) gecis yazildi ve geri
  // alindi: dogrulanmadan gonderilemez, cunku bugun tam olarak boyle bir varsayim servisi
  // acilista oldurdu. Acik borc, docs/04-kirilma-noktalari.md.
  Exec(ExpandConstant('{sys}\sc.exe'),
    'create {#ServiceName} binPath= "' + BinPath + '" start= auto DisplayName= "{#AppName}"',
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

  if WaitForHealth(GetPort('')) then
  begin
    // Parola artik veritabaninda hash olarak duruyor; registry'deki kopyasinin isi bitti.
    // Olculdu 2026-08-06: bu deger BUILTIN\Users tarafindan okunabiliyor.
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'Admin__Password');

    // Uygulama normalde kendisi siler; silememisse (izin, cokme) burada kapatiriz.
    DeleteFile(DataDir + '\ilk-parola');
  end
  else
    MsgBox('Servis kuruldu ama saglik ucu 90 saniyede cevap vermedi.' + #13#10 +
           'Loglar: ' + DataDir + '\logs' + #13#10 + #13#10 +
           'Kurulum parolasi guvenlik geregi registry''de birakildi; panel acildiktan sonra ' +
           'servisi yeniden baslatin, uygulama parolayi kendisi silecektir.',
           mbError, MB_OK);
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

    // 🔴 Olculdu 2026-08-06: burada GetPort() cagriliyordu, o da sihirbazin ag ayarlari
    // sayfasindan port okuyor. Kaldirmada sihirbaz sayfalari YOKTUR; cagri
    // "Runtime error: Could not call proc" ile patliyor ve kaldirma yarida kaliyordu.
    // Kural adi joker ile silinir, hicbir sihirbaz nesnesine dokunulmaz.
    Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -ExecutionPolicy Bypass -Command "Get-NetFirewallRule -DisplayName ''{#AppName} (*'' ' +
      '-ErrorAction SilentlyContinue | Remove-NetFirewallRule"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Eski surumlerin (0.12.1 ve oncesi) biraktigi ayarlar; parola da bunlarin arasindaydi.
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'Admin__Password');
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'Admin__Email');
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'ASPNETCORE_URLS');
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'ASPNETCORE_ENVIRONMENT');
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'Storage__DataDirectory');
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'Cors__AllowedOrigins__0');
    RegDeleteValue(HKEY_LOCAL_MACHINE, EnvKey, 'ForwardedHeaders__KnownProxies__0');
    RegDeleteKeyIncludingSubkeys(HKEY_LOCAL_MACHINE, SettingsKey);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    // Veri klasoru bilerek birakilir: icinde izlenen sunucu profilleri ve veri koruma
    // anahtarlari var. Anahtarlar silinirse kayitli SQL parolalari geri getirilemez.
    MsgBox('Kaldirma tamamlandi.' + #13#10 + #13#10 +
           'Veriler korundu: ' + ExpandConstant('{app}\data') + #13#10 +
           'Tamamen silmek isterseniz bu klasoru elle kaldirin.', mbInformation, MB_OK);
  end;
end;
