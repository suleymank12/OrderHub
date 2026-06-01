#requires -Version 7
<#
.SYNOPSIS
    Runs the test suite with coverage collection and produces an HTML report.

.DESCRIPTION
    Cross-platform (Windows / Linux pwsh). Self-contained: no shared helpers.
    1. Collects coverage via Coverlet's "XPlat Code Coverage" data collector.
    2. Converts the Cobertura XML into HTML + a text summary using ReportGenerator
       (pinned local tool, see .config/dotnet-tools.json).
    Report-only: this script does NOT enforce a coverage threshold. The phase
    quality gate lives in scripts/check-acceptance.ps1.

.OUTPUTS
    coverage/raw/**/coverage.cobertura.xml  (gitignored, raw collector output)
    coverage/html/index.html                (gitignored, human-readable report)
    Prints the text summary to stdout.

.NOTES
    Exit code 1 if any test fails; 0 otherwise.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Repo root is the parent of the scripts/ directory — works regardless of CWD.
$repoRoot = Split-Path -Parent $PSScriptRoot
$coverageRoot = Join-Path $repoRoot 'coverage'
$rawDir = Join-Path $coverageRoot 'raw'
$htmlDir = Join-Path $coverageRoot 'html'

Push-Location $repoRoot
try {
    # Clean previous run so stale Cobertura files never pollute the report glob.
    if (Test-Path $coverageRoot) {
        Remove-Item -Recurse -Force $coverageRoot
    }
    New-Item -ItemType Directory -Force -Path $rawDir | Out-Null

    Write-Host '==> Restoring local tools (ReportGenerator)...' -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed (exit $LASTEXITCODE)." }

    Write-Host '==> Running tests with coverage collection...' -ForegroundColor Cyan
    dotnet test `
        --collect:"XPlat Code Coverage" `
        --results-directory $rawDir
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) {
        Write-Host "==> Tests FAILED (exit $testExit). Skipping report generation." -ForegroundColor Red
        exit 1
    }

    Write-Host '==> Generating HTML + text summary report...' -ForegroundColor Cyan
    $reports = Join-Path $rawDir '**/coverage.cobertura.xml'
    dotnet tool run reportgenerator `
        "-reports:$reports" `
        "-targetdir:$htmlDir" `
        "-reporttypes:Html;TextSummary"
    if ($LASTEXITCODE -ne 0) { throw "ReportGenerator failed (exit $LASTEXITCODE)." }

    $summaryFile = Join-Path $htmlDir 'Summary.txt'
    if (Test-Path $summaryFile) {
        Write-Host ''
        Write-Host '================ Coverage Summary ================' -ForegroundColor Green
        Get-Content $summaryFile | Write-Host
        Write-Host '=================================================' -ForegroundColor Green
    }

    $indexHtml = Join-Path $htmlDir 'index.html'
    Write-Host ''
    Write-Host "HTML report: $indexHtml" -ForegroundColor Cyan
    exit 0
}
finally {
    Pop-Location
}
