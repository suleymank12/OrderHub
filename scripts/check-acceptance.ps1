#requires -Version 7
<#
.SYNOPSIS
    OrderHub faz geçiş kalite kapısı (Faz Geçiş Protokolü).

.DESCRIPTION
    Cross-platform (Windows / Linux pwsh). Self-contained: paylaşılan helper yok.
    Her faz geçişinde aynı kontroller tekrar koşar; CI tek satırla çağırır
    (pwsh -File scripts/check-acceptance.ps1). Çıktı Türkçe ve eyleme dönüktür.

    Kontroller:
      K1  — Dosya satır sayısı (kod dosyaları 400 satır limiti).
      K3  — Hardcoded secret taraması (kaynak + appsettings).
      LOCK— Her csproj'un packages.lock.json'u var mı (reproducible restore).
      BUILD — Solution derleniyor mu (0 warning, 0 error).
      TEST  — Tüm testler geçiyor mu (0 fail).
      DOCKER— docker compose dosyası syntax geçerli mi.

.NOTES
    Exit 0 → tüm kontroller geçti. Exit 1 → en az bir kontrol başarısız.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Derleyici çıktısını locale'den bağımsız parse edebilmek için İngilizce'ye sabitle.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'OrderHub.sln'
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $results.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    $mark = if ($Passed) { '✓' } else { '✗' }
    $color = if ($Passed) { 'Green' } else { 'Red' }
    Write-Host ("  {0}  {1,-28} {2}" -f $mark, $Name, $Detail) -ForegroundColor $color
}

# Bin/obj/migration/araç dizinlerini ve auto-generated dosyaları kaynak taramadan dışla.
$excludeDirs = @('bin', 'obj', '.git', '.vs', 'coverage', 'node_modules', 'TestResults')
function Get-SourceFiles {
    param([string[]]$Extensions)
    Get-ChildItem -Path $repoRoot -Recurse -File -Include $Extensions |
        Where-Object {
            $rel = $_.FullName.Substring($repoRoot.Length)
            $parts = $rel -split '[\\/]'
            -not ($parts | Where-Object { $excludeDirs -contains $_ }) -and
            $_.Name -notlike '*.Designer.cs' -and
            $rel -notmatch '[\\/]Migrations[\\/]'
        }
}

Write-Host ''
Write-Host '═══ OrderHub — Faz Geçiş Kontrolleri ═══' -ForegroundColor Cyan
Write-Host ''

# ── K1: 400 satır kuralı ────────────────────────────────────────────────────
Write-Host 'K1 — Dosya satır sayısı (limit 400):' -ForegroundColor Yellow
$offenders = Get-SourceFiles -Extensions '*.cs', '*.ps1' | ForEach-Object {
    $count = (Get-Content -LiteralPath $_.FullName | Measure-Object -Line).Lines
    if ($count -gt 400) { [pscustomobject]@{ File = $_.FullName.Substring($repoRoot.Length + 1); Lines = $count } }
}
if ($offenders) {
    foreach ($o in $offenders) {
        Write-Host ("      {0} → {1} satır (BÖL: sorumlulukları ayır)" -f $o.File, $o.Lines) -ForegroundColor Red
    }
    Add-Result 'K1 (400 satır)' $false ("{0} dosya limiti aşıyor" -f $offenders.Count)
}
else {
    Add-Result 'K1 (400 satır)' $true 'Tüm kod dosyaları ≤400 satır'
}

# ── K3: Hardcoded secret taraması ───────────────────────────────────────────
Write-Host 'K3 — Secret taraması:' -ForegroundColor Yellow
# Placeholder/sahte değer işaretleri — bunları içeren değerler secret sayılmaz.
$placeholders = @('changeme', 'change-me', 'your-', 'your_', 'placeholder', 'example',
    'sample', 'dummy', 'test', 'todo', 'replace', 'foo', 'bar', '<', '{', '$(')
$quotedPattern = '(?i)(password|secret|jwt.?secret|api.?key|token)["'']?\s*[:=]\s*["'']([^"'']{6,})["'']'
$connPattern = '(?i)password=([^;"''<${}\s]{6,})'
$secretFiles = Get-SourceFiles -Extensions '*.cs', 'appsettings*.json' |
    Where-Object { $_.Name -notlike '*.example' }
$leaks = [System.Collections.Generic.List[string]]::new()
foreach ($file in $secretFiles) {
    $lineNo = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNo++
        foreach ($m in ([regex]::Matches($line, $quotedPattern) + [regex]::Matches($line, $connPattern))) {
            $value = $m.Groups[$m.Groups.Count - 1].Value.ToLowerInvariant()
            if ($value -and -not ($placeholders | Where-Object { $value.Contains($_) })) {
                $leaks.Add(("{0}:{1}" -f $file.FullName.Substring($repoRoot.Length + 1), $lineNo))
            }
        }
    }
}
if ($leaks.Count -gt 0) {
    foreach ($leak in $leaks) {
        Write-Host ("      {0} → .env + ortam değişkenine taşı (K3)" -f $leak) -ForegroundColor Red
    }
    Add-Result 'K3 (secret)' $false ("{0} olası secret" -f $leaks.Count)
}
else {
    Add-Result 'K3 (secret)' $true 'Hardcoded secret bulunamadı'
}

# ── LOCK: packages.lock.json bütünlüğü ──────────────────────────────────────
Write-Host 'LOCK — Lock dosyası bütünlüğü:' -ForegroundColor Yellow
$csprojs = Get-SourceFiles -Extensions '*.csproj'
$missingLocks = $csprojs | Where-Object {
    -not (Test-Path (Join-Path $_.Directory.FullName 'packages.lock.json'))
}
if ($missingLocks) {
    foreach ($p in $missingLocks) {
        Write-Host ("      {0} → 'dotnet restore' ile lock üret" -f $p.Name) -ForegroundColor Red
    }
    Add-Result 'LOCK (lock dosyası)' $false ("{0} csproj eksik" -f $missingLocks.Count)
}
else {
    Add-Result 'LOCK (lock dosyası)' $true ("{0} csproj'un tümünde lock var" -f $csprojs.Count)
}

# ── BUILD: 0 warning, 0 error ───────────────────────────────────────────────
Write-Host 'BUILD — Derleme (0 warning / 0 error):' -ForegroundColor Yellow
$buildOut = & dotnet build $solution -c Debug --nologo 2>&1 | Out-String
$warnCount = if ($buildOut -match '(\d+)\s+Warning\(s\)') { [int]$Matches[1] } else { -1 }
$errCount = if ($buildOut -match '(\d+)\s+Error\(s\)') { [int]$Matches[1] } else { -1 }
if ($LASTEXITCODE -eq 0 -and $warnCount -eq 0 -and $errCount -eq 0) {
    Add-Result 'BUILD' $true '0 warning, 0 error'
}
else {
    Write-Host '      Derleme çıktısı (son satırlar):' -ForegroundColor Red
    ($buildOut -split "`n" | Select-Object -Last 8) | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
    Add-Result 'BUILD' $false ("warning={0}, error={1}, exit={2}" -f $warnCount, $errCount, $LASTEXITCODE)
}

# ── TEST: 0 fail ────────────────────────────────────────────────────────────
Write-Host 'TEST — Test paketi (0 fail):' -ForegroundColor Yellow
& dotnet test $solution -c Debug --no-build --nologo | Out-Null
if ($LASTEXITCODE -eq 0) {
    Add-Result 'TEST' $true 'Tüm testler geçti'
}
else {
    Add-Result 'TEST' $false ("dotnet test exit={0} — 'dotnet test' ile detayı gör" -f $LASTEXITCODE)
}

# ── DOCKER: compose syntax ──────────────────────────────────────────────────
Write-Host 'DOCKER — Compose syntax:' -ForegroundColor Yellow
$composeFile = Join-Path $repoRoot 'docker/docker-compose.yml'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Add-Result 'DOCKER (compose)' $false 'docker CLI bulunamadı — Docker Desktop kur/başlat'
}
else {
    & docker compose -f $composeFile config -q 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Add-Result 'DOCKER (compose)' $true 'compose dosyası geçerli'
    }
    else {
        Add-Result 'DOCKER (compose)' $false 'compose config hatası — docker compose config çıktısına bak'
    }
}

# ── Özet ────────────────────────────────────────────────────────────────────
$failed = @($results | Where-Object { -not $_.Passed })
Write-Host ''
Write-Host '═══ Sonuç ═══' -ForegroundColor Cyan
if ($failed.Count -eq 0) {
    Write-Host ("  ✓ Tüm kontroller geçti ({0}/{0}). Faz geçişi onaylı." -f $results.Count) -ForegroundColor Green
    exit 0
}
else {
    Write-Host ("  ✗ {0}/{1} kontrol başarısız. Yukarıdaki eylemleri uygula." -f $failed.Count, $results.Count) -ForegroundColor Red
    exit 1
}
