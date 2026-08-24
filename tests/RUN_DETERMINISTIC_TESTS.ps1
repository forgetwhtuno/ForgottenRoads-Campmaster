param(
    [string]$CscPath = ""
)

# Compiles only the Unity-free Campmaster core (models + session tracker +
# tests) into a temporary executable and runs it. It does not start Erenshor,
# reference the game assemblies, or install a plugin. A non-zero exit code
# means at least one named case failed.

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$modRoot = Split-Path -Parent $scriptRoot

function Find-Csc {
    if ($CscPath -and (Test-Path $CscPath)) { return (Resolve-Path $CscPath).Path }
    foreach ($candidate in @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "csc.exe was not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$csc = Find-Csc

$sourceFiles = @(
    (Join-Path $modRoot "src\CampModels.cs"),
    (Join-Path $modRoot "src\CampLivingModels.cs"),
    (Join-Path $modRoot "src\CampLivingActivityTracker.cs"),
    (Join-Path $modRoot "src\OptionalJournalBridge.cs"),
    (Join-Path $modRoot "src\CampLivingDeterministicTests.cs"),
    (Join-Path $modRoot "src\CampSessionTracker.cs"),
    (Join-Path $modRoot "src\RelaxModels.cs"),
    (Join-Path $modRoot "src\RelaxSessionTracker.cs"),
    (Join-Path $modRoot "src\RelaxDeterministicTests.cs"),
    (Join-Path $modRoot "src\SocialActivityTracker.cs"),
    (Join-Path $modRoot "src\SocialActivityDeterministicTests.cs"),
    (Join-Path $modRoot "src\SocialActivityFreshnessDeterministicTests.cs"),
    (Join-Path $modRoot "src\CampDeterministicTests.cs"),
    (Join-Path $scriptRoot "StandaloneCampTestsMain.cs")
)
foreach ($source in $sourceFiles) { if (-not (Test-Path $source)) { throw "Test source missing: $source" } }

# Static contracts that do not need game assemblies. Existing schema 3 must remain stable for
# current optional Deep Sims consumers; the new living-camp stream is additive and loader/game free.
$apiSource = Get-Content (Join-Path $modRoot "src\CampmasterApi.cs") -Raw
if ($apiSource -notmatch 'SchemaVersion\s*=\s*3' -or $apiSource -notmatch 'GetLivingEventsAfter' -or
    $apiSource -notmatch 'LivingOldestRetainedEventSequence' -or $apiSource -notmatch 'livingContractVersion') {
    throw "Campmaster living API/source compatibility contract failed."
}
$livingSource = (Get-Content (Join-Path $modRoot "src\CampLivingModels.cs") -Raw) + "`n" +
                (Get-Content (Join-Path $modRoot "src\CampLivingActivityTracker.cs") -Raw)
foreach ($token in @('UnityEngine', 'GameData', 'SimPlayer', 'Harmony', 'Lunaris')) {
    if ($livingSource -match [regex]::Escape($token)) { throw "Campmaster living core must stay pure; found $token." }
}
$allSource = (Get-ChildItem (Join-Path $modRoot "src") -Filter "*.cs" | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
if ($allSource -match 'using\s+(DeepSims|ErenshorJournal)') { throw "Campmaster optional-integration guard failed: hard AI/Journal dependency found." }
if ($allSource -match '\bOnGUI\b|\bGUILayout\b') { throw "Campmaster retained-UI guard failed: production OnGUI/GUILayout found." }
Write-Host "PASS: Campmaster living-camp source contracts"

$outputDir = Join-Path $env:TEMP ("CampmasterTests-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $outputDir | Out-Null
try {
    $output = Join-Path $outputDir "CampmasterDeterministicTests.exe"
    $arguments = @("/nologo", "/target:exe", "/optimize+", ('/out:{0}' -f $output)) + $sourceFiles
    & $csc $arguments
    if ($LASTEXITCODE -ne 0) { throw "Campmaster test compilation failed." }
    & $output
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $outputDir) { Remove-Item -LiteralPath $outputDir -Recurse -Force }
}
