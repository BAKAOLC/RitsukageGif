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
if (!(Test-Path -LiteralPath (Join-Path $build 'CMakeCache.txt'))) {
    & cmake @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Barcode native configuration failed.' }
}
& cmake --build $build --config Release --target ZXing --parallel
if ($LASTEXITCODE -ne 0) { throw 'Barcode native compilation failed.' }
$source = Join-Path $build '_deps/zxing-src'
$license = [IO.File]::ReadAllText((Join-Path $source 'LICENSE')) + "`n`n" + [IO.File]::ReadAllText((Join-Path $root 'native/barcode/libzueci.LICENSE'))
[IO.File]::WriteAllText((Join-Path $build 'bin/RitsukageBarcode.LICENSE'), $license, [Text.UTF8Encoding]::new($false))
