# Read-only security audit of an installed panel. Changes nothing — it measures, so the
# findings in docs/04-kirilma-noktalari.md stop being assumptions about Windows defaults.
#
#   .\windows-guvenlik-denetimi.ps1
#   .\windows-guvenlik-denetimi.ps1 -Port 5199 -SkipRateLimitTest
#
# Run as administrator: some checks read the service configuration and the registry.

[CmdletBinding()]
param(
    [int]$Port = 5199,
    [string]$ServiceName = 'SunucuIzleme',
    [string]$DataDirectory = 'C:\ProgramData\SunucuIzleme',
    [switch]$SkipRateLimitTest
)

$ErrorActionPreference = 'Continue'
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param($Id, $Title, $Verdict, $Detail)
    $results.Add([pscustomobject]@{ Bulgu = $Id; Konu = $Title; Sonuc = $Verdict; Ayrinti = $Detail })
}

# --- 1. Is the admin password still sitting in the machine environment? --------------------
$envKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
$adminPassword = (Get-ItemProperty -Path $envKey -Name 'Admin__Password' -ErrorAction SilentlyContinue).'Admin__Password'

if ($adminPassword) {
    # Who can read that key? Non-admin read access is what turns this into a finding.
    $envAcl = (Get-Acl $envKey).Access |
        Where-Object { $_.IdentityReference -match 'Users|Everyone|INTERACTIVE|Authenticated' -and $_.RegistryRights -match 'Read' } |
        ForEach-Object { $_.IdentityReference.Value } | Sort-Object -Unique
    $who = if ($envAcl) { $envAcl -join ', ' } else { 'yalnizca ayricalikli hesaplar' }
    Add-Result 1 'Admin parolasi ortam degiskeninde' 'ACIK' "$($adminPassword.Length) karakter, duz metin. Okuyabilenler: $who"
} else {
    Add-Result 1 'Admin parolasi ortam degiskeninde' 'TEMIZ' 'Admin__Password kaydi yok'
}

# --- 2. Kestrel binding and firewall exposure ----------------------------------------------
$urls = (Get-ItemProperty -Path $envKey -Name 'ASPNETCORE_URLS' -ErrorAction SilentlyContinue).'ASPNETCORE_URLS'
$listening = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty LocalAddress -Unique

if ($urls -match '0\.0\.0\.0' -or $listening -contains '0.0.0.0' -or $listening -contains '::') {
    $rule = Get-NetFirewallRule -ErrorAction SilentlyContinue |
        Where-Object DisplayName -like "*$Port*" |
        Select-Object -First 1
    $scope = if ($rule) { ($rule | Get-NetFirewallAddressFilter).RemoteAddress -join ', ' } else { 'kural yok' }
    Add-Result 2 'Kestrel dogrudan aga acik' 'ACIK' "URLS=$urls; guvenlik duvari kaynak kisiti: $scope"
} else {
    Add-Result 2 'Kestrel dogrudan aga acik' 'TEMIZ' "Yalniz loopback: $urls"
}

# --- 3. Who can read the database and the data-protection key ring? ------------------------
foreach ($path in @($DataDirectory, (Join-Path $DataDirectory 'keys'), (Join-Path $DataDirectory 'mssqlrealtime.db'))) {
    if (-not (Test-Path $path)) { continue }

    $weak = (Get-Acl $path).Access |
        Where-Object {
            $_.IdentityReference -match 'BUILTIN\\Users|Everyone|Authenticated Users|INTERACTIVE' -and
            $_.FileSystemRights -match 'Read|FullControl|Modify' -and
            $_.AccessControlType -eq 'Allow'
        } |
        ForEach-Object { "$($_.IdentityReference.Value)=$($_.FileSystemRights)" } | Sort-Object -Unique

    $leaf = Split-Path $path -Leaf
    if ($weak) {
        Add-Result 3 "Erisim: $leaf" 'ACIK' ($weak -join '; ')
    } else {
        Add-Result 3 "Erisim: $leaf" 'TEMIZ' 'Yalniz SYSTEM/Administrators'
    }
}

# --- 4. Which account runs the service? ----------------------------------------------------
$svc = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
if ($svc) {
    $verdict = if ($svc.StartName -match 'LocalSystem') { 'ACIK' } else { 'TEMIZ' }
    Add-Result 4 'Servis hesabi' $verdict $svc.StartName
} else {
    Add-Result 4 'Servis hesabi' 'YOK' "Servis bulunamadi: $ServiceName"
}

# --- 5. Passwords in the log files ----------------------------------------------------------
$logs = Get-ChildItem (Join-Path $DataDirectory 'logs') -Filter '*.log' -ErrorAction SilentlyContinue
$hits = $logs | Select-String -Pattern 'Parola\s*:', 'password=', 'bot\d+:' -ErrorAction SilentlyContinue
if ($hits) {
    Add-Result 5 'Loglarda sir' 'ACIK' "$($hits.Count) satir: $(($hits | Select-Object -First 1).Filename)"
} else {
    Add-Result 5 'Loglarda sir' 'TEMIZ' "$($logs.Count) log dosyasi tarandi"
}

# --- 6. Security headers ---------------------------------------------------------------------
try {
    $response = Invoke-WebRequest "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec 10
    $missing = @('X-Content-Type-Options', 'X-Frame-Options', 'Content-Security-Policy', 'Referrer-Policy') |
        Where-Object { -not $response.Headers.ContainsKey($_) }
    if ($missing) {
        Add-Result 6 'Guvenlik basliklari' 'ACIK' "Eksik: $($missing -join ', ')"
    } else {
        Add-Result 6 'Guvenlik basliklari' 'TEMIZ' 'Dordu de var'
    }
} catch {
    Add-Result 6 'Guvenlik basliklari' 'OLCULEMEDI' $_.Exception.Message
}

# --- 2b. Does a forged X-Forwarded-For dodge the rate limiter? ------------------------------
# 12 sign-in attempts with a fresh fake client address each time: if the limiter partitions on
# the forged header, the 429 that a real attacker would hit never arrives.
if (-not $SkipRateLimitTest) {
    $codes = @()
    foreach ($i in 1..12) {
        try {
            $null = Invoke-WebRequest "http://127.0.0.1:$Port/api/auth/login" -Method Post `
                -UseBasicParsing -TimeoutSec 10 `
                -ContentType 'application/json' `
                -Headers @{ 'X-Forwarded-For' = "203.0.113.$i" } `
                -Body '{"email":"denetim@example.com","password":"kesinlikle-yanlis-parola"}'
        } catch {
            $codes += [int]$_.Exception.Response.StatusCode
        }
    }

    if ($codes -contains 429) {
        Add-Result '2b' 'X-Forwarded-For ile hiz siniri' 'TEMIZ' "429 geldi ($(($codes | Where-Object { $_ -eq 429 }).Count)/12)"
    } else {
        Add-Result '2b' 'X-Forwarded-For ile hiz siniri' 'ACIK' "12 denemede 429 yok. Kodlar: $(($codes | Sort-Object -Unique) -join ', ')"
    }
}

# --- 7. Does the panel answer from outside? -------------------------------------------------
$ip = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notmatch '^127\.' -and $_.PrefixOrigin -ne 'WellKnown' } |
    Select-Object -First 1).IPAddress
if ($ip) {
    try {
        $null = Invoke-WebRequest "http://${ip}:$Port/api/health" -UseBasicParsing -TimeoutSec 5
        Add-Result 7 'LAN uzerinden erisim' 'ACIK' "http://${ip}:$Port cevap veriyor (duz HTTP)"
    } catch {
        Add-Result 7 'LAN uzerinden erisim' 'TEMIZ' "http://${ip}:$Port cevap vermiyor"
    }
}

Write-Host ''
Write-Host "Sunucu Izleme — guvenlik denetimi  $(Get-Date -Format 'yyyy-MM-dd HH:mm')" -ForegroundColor Cyan
Write-Host ''
$results | Format-Table -AutoSize -Wrap
Write-Host ''
Write-Host "ACIK = bulgu dogrulandi, TEMIZ = sorun yok. Ciktiyi oldugu gibi paylasin." -ForegroundColor DarkGray
