# Installs the monitoring panel as a Windows service.
# Run in an elevated PowerShell, from the folder containing MssqlRealtime.Api.exe.
#
#   .\windows-kur.ps1 -AdminPassword 'guclu-parola'
#   .\windows-kur.ps1 -AdminPassword '...' -Port 5199 -PublicOrigin 'https://izleme.firma.com'
#   .\windows-kur.ps1 -AdminPassword '...' -Account 'DOMAIN\svc_izleme'   # Windows kimlik dogrulamasi icin
#
# -Account is only needed when the monitored SQL Servers use Windows (integrated)
# authentication; with SQL logins the default LocalSystem is enough and grants the service
# no domain rights.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AdminPassword,
    [string]$AdminEmail = 'admin@local',
    [int]$Port = 5199,
    [string]$PublicOrigin,
    # Only needed when the reverse proxy runs on another machine: X-Forwarded-For is believed
    # from loopback and from this address, nowhere else.
    [string]$ProxyAddress,
    [string]$ServiceName = 'SunucuIzleme',
    [string]$DisplayName = 'Sunucu Izleme Paneli',
    # Kurulum klasorunun altinda: Program Files ve ProgramData izinleri servisin kendi
    # veritabanini acamamasina kadar gitti (0.12.1-0.12.4).
    [string]$DataDirectory,
    [string]$Account,
    [SecureString]$AccountPassword
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bu betik yonetici olarak calistirilmali (PowerShell -> Run as Administrator).'
}

if ($AdminPassword.Length -lt 10) {
    throw 'Parola en az 10 karakter olmali.'
}

$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
if (-not $DataDirectory) { $DataDirectory = Join-Path $root 'data' }
$exe = Join-Path $root 'MssqlRealtime.Api.exe'

if (-not (Test-Path $exe)) {
    throw "MssqlRealtime.Api.exe bulunamadi: $root"
}

# The data directory holds the database and the data-protection key ring; losing the latter
# makes every stored SQL password unreadable, so it lives outside the program folder and
# survives an in-place upgrade.
New-Item -ItemType Directory -Force -Path $DataDirectory | Out-Null

# Bind to all interfaces when a public origin is set (something proxies to us); otherwise
# loopback only, so a default install is not exposed on the LAN by accident.
$urls = if ($PublicOrigin) { "http://0.0.0.0:$Port" } else { "http://127.0.0.1:$Port" }

# Measured 2026-08-06: a service does not see machine environment variables written after
# boot, so the settings that must reach it travel in the service's command line instead (see
# $binaryPath below). These stay for anyone running the exe by hand.
$machine = [EnvironmentVariableTarget]::Machine
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', $machine)
[Environment]::SetEnvironmentVariable('Storage__DataDirectory', $DataDirectory, $machine)
[Environment]::SetEnvironmentVariable('Admin__Email', $AdminEmail, $machine)

# The password never goes to the registry: BUILTIN\Users could read it there. It waits in the
# locked-down data directory until the app creates the account and deletes it.
Set-Content -Path (Join-Path $DataDirectory 'ilk-parola') -Value $AdminPassword -NoNewline -Encoding utf8

if ($PublicOrigin) {
    # Must match the address the phone connects to, scheme included, or sign-in fails on CORS.
    [Environment]::SetEnvironmentVariable('Cors__AllowedOrigins__0', $PublicOrigin, $machine)
}

# Believed only from here and from loopback. Measured 2026-08-06: trusting the header from
# anyone let a forged X-Forwarded-For walk past the sign-in rate limiter, twelve attempts
# without a single 429.
[Environment]::SetEnvironmentVariable(
    'ForwardedHeaders__KnownProxies__0',
    $(if ($ProxyAddress) { $ProxyAddress } else { $null }),
    $machine)

if ($PublicOrigin -and -not $ProxyAddress) {
    Write-Warning ("Ters vekil sunucu adresi verilmedi. Vekil bu makinede degilse " +
        "-ProxyAddress <ip> ile verin; verilmezse istemci adresleri vekilin adresi gorunur.")
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Mevcut servis durduruluyor ve kaldiriliyor: $ServiceName"
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Servis olusturuluyor: $ServiceName"

# Everything the service must know, on the one channel it always receives.
$binaryPath = "`"$exe`" --Storage:DataDirectory=`"$DataDirectory`" --urls=`"$urls`""
if ($PublicOrigin) { $binaryPath += " --Cors:AllowedOrigins:0=`"$PublicOrigin`"" }
if ($ProxyAddress) { $binaryPath += " --ForwardedHeaders:KnownProxies:0=`"$ProxyAddress`"" }

$arguments = @{
    Name           = $ServiceName
    BinaryPathName = $binaryPath
    DisplayName    = $DisplayName
    Description    = 'MSSQL ve site izleme paneli. Izlenen sunuculara yalnizca salt okunur baglanir.'
    StartupType    = 'Automatic'
}

if ($Account) {
    if (-not $AccountPassword) {
        $AccountPassword = Read-Host -AsSecureString "$Account hesabinin parolasi"
    }
    $arguments.Credential = New-Object System.Management.Automation.PSCredential($Account, $AccountPassword)
}

New-Service @arguments | Out-Null

# Restart on failure: a monitoring panel that quietly stopped is the one failure nobody notices.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

# Open the port only when something outside this machine has to reach it.
if ($PublicOrigin) {
    $ruleName = "$DisplayName ($Port)"
    if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP `
            -LocalPort $Port -Action Allow -Profile Domain, Private | Out-Null
        Write-Host "Guvenlik duvari kurali eklendi: $ruleName (Domain + Private)"
    }
}

Write-Host 'Servis baslatiliyor…'
Start-Service -Name $ServiceName

$deadline = (Get-Date).AddSeconds(60)
$ok = $false

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    try {
        $response = Invoke-RestMethod "http://127.0.0.1:$Port/api/health" -TimeoutSec 5
        if ($response.status -eq 'ok') { $ok = $true; break }
    } catch {
        # Not up yet; keep waiting until the deadline.
    }
}

Write-Host ''

if ($ok) {
    # The app deletes the password file itself once the account exists; close it here too in
    # case it could not. Also clears the registry copy that 0.12.1 and earlier left behind.
    Remove-Item (Join-Path $DataDirectory 'ilk-parola') -Force -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable('Admin__Password', $null, $machine)

    Write-Host "✅ Panel calisiyor: http://127.0.0.1:$Port"
    Write-Host "   Kullanici: $AdminEmail"
    if ($PublicOrigin) {
        Write-Host "   Genel adres: $PublicOrigin  (ters vekil sunucunun bu makineye $Port portundan yonlendirmesi gerekir)"
    }
    Write-Host ''
    Write-Host 'Sonraki adim: panele girip izlenecek SQL Server adresini ekleyin.'
    Write-Host 'Izlenen sunucuya hicbir sey kurulmaz; SQL tarafinda salt okunur bir kullanici yeterlidir:'
    Write-Host '   CREATE LOGIN [izleme] WITH PASSWORD = N''...'';'
    Write-Host '   GRANT VIEW SERVER STATE TO [izleme];'
    Write-Host '   GRANT VIEW ANY DEFINITION TO [izleme];'
    Write-Host '   GRANT ALTER ANY CONNECTION TO [izleme];   -- yalnizca oturum sonlandirma (KILL) kullanacaksaniz'
} else {
    Write-Warning "Servis basladi ama saglik ucu 60 saniyede cevap vermedi."
    Write-Host "Durum: $((Get-Service -Name $ServiceName).Status)"
    Write-Host "Loglar: $DataDirectory\logs\"
    Write-Host "Olay Goruntuleyici: Windows Logs -> Application, kaynak: $ServiceName"
}

Write-Host ''
Write-Host 'Yonetim komutlari:'
Write-Host "   Get-Service $ServiceName"
Write-Host "   Restart-Service $ServiceName"
Write-Host "   Get-Content '$DataDirectory\logs\app-*.log' -Tail 30 -Wait"
