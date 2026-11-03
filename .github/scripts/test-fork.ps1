# Runs the tests of the four packages the fork publishes (IO.Slk, IO.Mpq, Build.Core, Build) and fails on a test that is not in
# .github/known-failures/<project>.txt. War3Net.Build.Core.Tests has 12 cases that fail on Drake53's unchanged master as well
# (data-driven cases on real map files, most of them marked flaky); the list is what they are, so a new failure still fails the run.
$ErrorActionPreference = 'Stop'

$projects = 'War3Net.IO.Slk.Tests', 'War3Net.IO.Mpq.Tests', 'War3Net.Build.Tests', 'War3Net.Build.Core.Tests'
$unexpected = 0
New-Item -ItemType Directory -Force TestResults | Out-Null

foreach ($project in $projects) {
    $trx = "$project.trx"
    Remove-Item -LiteralPath "TestResults/$trx" -ErrorAction SilentlyContinue
    dotnet test "tests/$project" --logger "trx;LogFileName=$trx" --results-directory TestResults
    if (-not (Test-Path "TestResults/$trx")) {
        throw "$project wrote no result file (it did not build or start)"
    }

    $known = @()
    $knownFile = ".github/known-failures/$project.txt"
    if (Test-Path $knownFile) {
        $known = @(Get-Content $knownFile)
    }

    $failed = @(& "$PSScriptRoot/failed-tests.ps1" "TestResults/$trx")
    $new = @($failed | Where-Object { $known -notcontains $_ })
    Write-Host "$project`: $($failed.Count) failed, $($known.Count) known, $($new.Count) not known"
    if ($new.Count -gt 0) {
        $new | ForEach-Object { Write-Host "  NEW FAILURE $_" }
        $unexpected += $new.Count
    }
}

if ($unexpected -gt 0) {
    throw "$unexpected failed tests are not in .github/known-failures"
}

# dotnet test exits with 1 for the known failures, and PowerShell would pass that on as the script's exit code.
exit 0
