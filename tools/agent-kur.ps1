# Installs the monitoring agent as a Windows service.
# Run in an elevated PowerShell, from the folder containing MssqlRealtime.Agent.exe.
#
#   .\agent-kur.ps1
#   .\agent-kur.ps1 -ServiceName "MssqlRealtimeAgent" -Account "DOMAIN\svc_izleme"
#
# -Account is only needed for Windows (integrated) authentication to SQL Server; with a SQL
# login the default LocalSystem is enough and grants the service no domain rights.

[CmdletBinding()]
param(
    [string]$ServiceName = 'MssqlRealtimeAgent',
    [string]$DisplayName = 'Sunucu Izleme Agent',
    [string]$Account,
    [SecureString]$AccountPassword
)

$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Bu betik yonetici olarak calistirilmali (PowerShell -> Run as Administrator).'
    }
}

Assert-Admin

$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
$exe = Join-Path $root 'MssqlRealtime.Agent.exe'

if (-not (Test-Path $exe)) {
    throw "MssqlRealtime.Agent.exe bulunamadi: $root"
}

# Refuse to install with the placeholder still in place — otherwise the service starts, fails
# to register, and the problem only surfaces later in a log nobody is watching yet.
$configPath = Join-Path $root 'appsettings.json'
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    $key = $config.Agent.EnrollmentKey

    if ([string]::IsNullOrWhiteSpace($key) -or $key -like '*BURAYA*') {
        throw "appsettings.json icindeki EnrollmentKey doldurulmamis. Merkezi arayuzde Agent'lar -> Yeni agent ile uretin."
    }

    if ($config.Agent.HubUrl -notlike 'https://*') {
        Write-Warning "HubUrl https:// degil. Kayit anahtari ve SQL parolasi ag uzerinde ACIK gidecek."
    }
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Mevcut servis durduruluyor ve kaldiriliyor: $ServiceName"
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Servis olusturuluyor: $ServiceName"

$arguments = @{
    Name           = $ServiceName
    BinaryPathName = "`"$exe`""
    DisplayName    = $DisplayName
    Description    = 'SQL Server olcumlerini merkezi izleme sunucusuna gonderir (yalnizca disa dogru baglanti).'
    StartupType    = 'Automatic'
}

if ($Account) {
    if (-not $AccountPassword) {
        $AccountPassword = Read-Host -AsSecureString "$Account hesabinin parolasi"
    }
    $arguments.Credential = New-Object System.Management.Automation.PSCredential($Account, $AccountPassword)
}

New-Service @arguments | Out-Null

# Restart on failure rather than staying down: an agent that quietly stopped is exactly the
# situation the hub's "agent silent" alert exists for, but not stopping is better.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

Write-Host 'Servis baslatiliyor…'
Start-Service -Name $ServiceName
Start-Sleep -Seconds 5

$service = Get-Service -Name $ServiceName
Write-Host ''
Write-Host "Durum: $($service.Status)"

$log = Get-ChildItem (Join-Path $root 'logs') -Filter 'agent-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($log) {
    Write-Host ''
    Write-Host "Son log satirlari ($($log.Name)):"
    Get-Content $log.FullName -Tail 10 | ForEach-Object { Write-Host "   $_" }
    Write-Host ''
    Write-Host '"Kayit basarili" goruyorsaniz kurulum tamamdir.'
} else {
    Write-Host ''
    Write-Host 'Log dosyasi henuz olusmadi; birkac saniye sonra logs\ klasorune bakin.'
}

Write-Host ''
Write-Host 'Yonetim komutlari:'
Write-Host "   Get-Service $ServiceName"
Write-Host "   Restart-Service $ServiceName"
Write-Host "   Get-Content .\logs\agent-*.log -Tail 30 -Wait"
