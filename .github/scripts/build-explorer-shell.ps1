param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$BuildDirectory = 'artifacts/explorer-shell-build'
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'The Explorer extension requires Windows.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$buildPath = [IO.Path]::GetFullPath($BuildDirectory, $root)
$destinationPath = [IO.Path]::GetFullPath($Destination, $root)
New-Item -ItemType Directory -Force -Path $buildPath, $destinationPath | Out-Null
$generator = if (Test-Path -LiteralPath 'C:/Program Files/Microsoft Visual Studio/18') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
& cmake -S (Join-Path $root 'native/windows/explorer') -B $buildPath -G $generator -A x64
if ($LASTEXITCODE -ne 0) { throw 'Explorer extension configuration failed.' }
& cmake --build $buildPath --config Release
if ($LASTEXITCODE -ne 0) { throw 'Explorer extension build failed.' }
Copy-Item -LiteralPath (Join-Path $buildPath 'Release/RitsukageGif.ExplorerShell.dll') -Destination $destinationPath -Force
