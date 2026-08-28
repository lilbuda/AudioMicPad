$ErrorActionPreference = 'Stop'

& "$PSScriptRoot\build.ps1"

$compilerCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$compiler = $compilerCandidates | Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup was not found. Install it from https://jrsoftware.org/isdl.php and run this script again.'
}

& $compiler "$PSScriptRoot\setup\AudioMicPad.iss"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = "$PSScriptRoot\installer\AudioMicPad-Setup-v1.1.3.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The installer was not created at $installerPath."
}

Write-Host "Built: $installerPath"
