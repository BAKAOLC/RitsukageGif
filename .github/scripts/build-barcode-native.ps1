param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-arm64')]
    [string]$RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if ($RuntimeIdentifier -ne "$platform-$architecture") { throw 'Barcode native compilation requires a matching host OS and architecture.' }
$build = Join-Path $root "artifacts/native-barcode/$RuntimeIdentifier"
$arguments = @('-S', (Join-Path $root 'native/barcode'), '-B', $build, '-DCMAKE_BUILD_TYPE=Release')
if ($IsWindows) { $arguments += @('-A', 'x64') }
if ($IsMacOS) { $arguments += @('-DCMAKE_OSX_DEPLOYMENT_TARGET=13.0', '-DCMAKE_OSX_ARCHITECTURES=arm64') }
[IO.Directory]::CreateDirectory($build) | Out-Null
$lockPath = Join-Path $build '.build.lock'
$wait = [Diagnostics.Stopwatch]::StartNew()
while ($true) {
    try {
        $buildLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        break
    }
    catch [IO.IOException] {
        if ($wait.Elapsed.TotalMinutes -ge 10) { throw }
        Start-Sleep -Milliseconds 250
    }
}
try {
    if (!(Test-Path -LiteralPath (Join-Path $build 'CMakeCache.txt'))) {
        & cmake @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Barcode native configuration failed.' }
    }
    & cmake --build $build --config Release --target ZXing --parallel
    if ($LASTEXITCODE -ne 0) { throw 'Barcode native compilation failed.' }
    $source = Join-Path $build '_deps/zxing-src'
    $license = [IO.File]::ReadAllText((Join-Path $source 'LICENSE')) + "`n`n" + [IO.File]::ReadAllText((Join-Path $root 'native/barcode/libzueci.LICENSE'))
    $license += "`n`nLocal modifications: the Telepen byte comparison uses an explicit uint8_t value; QR finder-pattern candidate capacity scales with the number of detected finder patterns.`n"
    $licensePath = Join-Path $build 'bin/RitsukageBarcode.LICENSE'
    if (![IO.File]::Exists($licensePath) -or [IO.File]::ReadAllText($licensePath) -cne $license) {
        [IO.File]::WriteAllText($licensePath, $license, [Text.UTF8Encoding]::new($false))
    }
}
finally {
    $buildLock.Dispose()
}
