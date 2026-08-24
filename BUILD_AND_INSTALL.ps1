param(
    [string]$GameDir = "",
    [string]$LunarisLibDir = "",
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Game([string]$Explicit) {
    if ($Explicit -and (Test-Path (Join-Path $Explicit "Erenshor.exe"))) {
        return (Resolve-Path $Explicit).Path
    }

    $candidates = @()
    if (${env:ProgramFiles(x86)}) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} "Steam\steamapps\common\Erenshor"
    }
    if ($env:ProgramFiles) {
        $candidates += Join-Path $env:ProgramFiles "Steam\steamapps\common\Erenshor"
    }

    foreach ($root in @((Join-Path ${env:ProgramFiles(x86)} "Steam"), (Join-Path $env:ProgramFiles "Steam"))) {
        if (-not $root) { continue }
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object {
                $library = $_.Groups[1].Value -replace '\\\\','\'
                $candidates += [IO.Path]::Combine($library, "steamapps", "common", "Erenshor")
            }
        }
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path (Join-Path $candidate "Erenshor.exe")) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Erenshor installation not found. Pass -GameDir 'C:\path\to\Erenshor'."
}

function Find-LunarisLibDir([string]$Explicit, [string]$Game) {
    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    $candidates += (Join-Path $ScriptRoot "LunarisLibs")
    $candidates += $Game

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not $candidate) { continue }
        if ((Test-Path (Join-Path $candidate "Lunaris.dll")) -and (Test-Path (Join-Path $candidate "0Harmony.dll"))) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Could not find Lunaris developer references. Put Lunaris.dll and 0Harmony.dll in '$ScriptRoot\LunarisLibs' or pass -LunarisLibDir."
}

function Find-Csc {
    foreach ($path in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )) {
        if (Test-Path $path) { return $path }
    }
    throw "csc.exe not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$GameDir = Find-Game $GameDir
$LunarisLibDir = Find-LunarisLibDir $LunarisLibDir $GameDir
$csc = Find-Csc
$managed = Join-Path $GameDir "Erenshor_Data\Managed"
$pluginRoot = Join-Path $GameDir "plugins"
$buildOutput = Join-Path $ScriptRoot "build-output"
New-Item -ItemType Directory -Force -Path $buildOutput | Out-Null
if (-not $BuildOnly) { New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null }

$refs = @(
    (Join-Path $LunarisLibDir "Lunaris.dll"),
    (Join-Path $LunarisLibDir "0Harmony.dll"),
    (Join-Path $managed "Assembly-CSharp.dll"),
    (Join-Path $managed "netstandard.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.UIModule.dll"),
    (Join-Path $managed "UnityEngine.UI.dll"),
    (Join-Path $managed "UnityEngine.IMGUIModule.dll"),
    (Join-Path $managed "UnityEngine.TextRenderingModule.dll")
)

foreach ($ref in $refs) {
    if (-not (Test-Path $ref)) {
        throw "Missing reference: $ref"
    }
}

$TempDir = Join-Path $env:TEMP ("ErenshorCampmaster-build-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
$TempDll = Join-Path $TempDir "ErenshorCampmaster.dll"
$rsp = Join-Path $TempDir "ErenshorCampmaster.rsp"
$out = Join-Path $pluginRoot "ErenshorCampmaster.dll"
$candidate = Join-Path $buildOutput "ErenshorCampmaster.dll"

try {
    $lines = @(
        '/nologo',
        '/target:library',
        '/optimize+',
        ('/out:"{0}"' -f $TempDll)
    )
    $refs | ForEach-Object { $lines += ('/reference:"{0}"' -f $_) }
    Get-ChildItem (Join-Path $ScriptRoot "src") -Filter "*.cs" | Sort-Object Name | ForEach-Object {
        $lines += '"' + $_.FullName + '"'
    }
    $fallbackUi = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptRoot)) "Erenshor-Mod-Suite\shared\ErenshorSuite.UI\StandaloneFallbackUi.cs"
    if (-not (Test-Path -LiteralPath $fallbackUi)) { throw "Missing shared standalone UI source: $fallbackUi" }
    $lines += '"' + $fallbackUi + '"'
    $lines | Set-Content $rsp -Encoding ASCII

    $lunarisHash = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $LunarisLibDir "Lunaris.dll")).Hash.ToLowerInvariant()
    Write-Host "Building Erenshor Campmaster as a native Lunaris plugin..." -ForegroundColor Cyan
    Write-Host "  Game:    $GameDir"
    Write-Host "  Lunaris: $LunarisLibDir\Lunaris.dll ($lunarisHash)"
    & $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed. Copy the compiler errors and send them back for correction."
    }
    if (-not (Test-Path $TempDll)) { throw "Compiler reported success but did not produce $TempDll" }

    Copy-Item -LiteralPath $TempDll -Destination $candidate -Force
}
finally {
    if (Test-Path $TempDir) { Remove-Item -LiteralPath $TempDir -Recurse -Force -ErrorAction SilentlyContinue }
}

$builtHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate).Hash.ToLowerInvariant()
Write-Host "  Candidate: $candidate"
Write-Host "  SHA256:    $builtHash"
if ($BuildOnly) {
    Write-Host "Campmaster compiled successfully (BuildOnly - nothing installed)." -ForegroundColor Green
    exit 0
}
Copy-Item -LiteralPath $candidate -Destination $out -Force
$installedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $out).Hash.ToLowerInvariant()
if ($installedHash -ne $builtHash) { throw "Installed Campmaster DLL hash does not match the fresh candidate." }
Write-Host "Installed Erenshor Campmaster to $out" -ForegroundColor Green
Write-Host "  Installed SHA256: $installedHash"
