param(
    [string]$Root = "C:\Users\User\Downloads\RESULTS\RESULTS",
    [int]$Year = 2026,
    [string]$Baseline = "$PSScriptRoot\baseline.txt",
    [switch]$Accept
)

# Parses every workbook under $Root and reduces each to one line, so a regression
# anywhere in the corpus shows up as a diff rather than a wall of text.
$raw = dotnet run --project $PSScriptRoot -- $Root $Year 2>&1

$lines = @()
$file = $null
foreach ($l in $raw) {
    $s = "$l"
    if ($s -match '^={4,}\s+(.+?)\s+={4,}$') { $file = $Matches[1]; continue }
    if ($s -match '^\s+format=(\S+)\s+match=(\S+)') { $fmt = $Matches[1]; $match = $Matches[2]; continue }
    if ($s -match '^\s+placements=(\d+)\s+educationAssets=(\d+)\s+warnings=(\d+)') {
        $lines += "{0,-58} {1,-22} {2,-8} P={3,-4} E={4,-4} W={5}" -f $file, $fmt, $match, $Matches[1], $Matches[2], $Matches[3]
    }
}

$current = $lines | Sort-Object
$current | Write-Host

$totP = ($current | ForEach-Object { if ($_ -match 'P=(\d+)') { [int]$Matches[1] } } | Measure-Object -Sum).Sum
$totE = ($current | ForEach-Object { if ($_ -match 'E=(\d+)') { [int]$Matches[1] } } | Measure-Object -Sum).Sum
$bad = $current | Where-Object { $_ -match 'generic|unrecognised' -or $_ -match 'P=0\s+E=0' }

Write-Host ""
Write-Host ("files={0}  placements={1}  education={2}  notParsed={3}" -f $current.Count, $totP, $totE, $bad.Count)
if ($bad) { Write-Host "NOT PARSED:"; $bad | Write-Host }

if ($Accept) {
    $current | Set-Content $Baseline
    Write-Host "baseline written to $Baseline"
    exit 0
}

if (-not (Test-Path $Baseline)) { Write-Host "no baseline yet - re-run with -Accept"; exit 0 }

$diff = Compare-Object (Get-Content $Baseline) $current
if ($diff) {
    Write-Host ""
    Write-Host "CHANGED vs baseline:"
    $diff | ForEach-Object { "{0} {1}" -f $_.SideIndicator, $_.InputObject } | Write-Host
    exit 1
}
Write-Host "matches baseline"
exit 0
