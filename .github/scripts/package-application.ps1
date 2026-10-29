param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [string]$Output = 'artifacts/packages'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$versions = & (Join-Path $PSScriptRoot 'get-package-version.ps1')
$architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$hostPlatform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if ($RuntimeIdentifier -ne "$hostPlatform-$architecture") { throw 'Native AOT packaging requires a matching host OS and architecture.' }
$artifacts = Join-Path $root 'artifacts'
$staging = [IO.Path]::GetFullPath((Join-Path $artifacts "application-$RuntimeIdentifier"))
$outputPath = [IO.Path]::GetFullPath($Output, $root)
if (!$staging.StartsWith([IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging directory.' }
if ($outputPath -eq $staging -or $outputPath.StartsWith($staging + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The output must be outside the staging directory.' }
if (Test-Path -LiteralPath $staging) { [IO.Directory]::Delete($staging, $true) }
New-Item -ItemType Directory -Force -Path $staging, $outputPath | Out-Null
$publish = Join-Path $staging 'publish'
if ($IsMacOS) { $env:MACOSX_DEPLOYMENT_TARGET = '13.0' }
& dotnet publish (Join-Path $root 'src/RitsukageGif') -c Release -r $RuntimeIdentifier -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
$executable = Join-Path $publish $(if ($IsWindows) { 'RitsukageGif.exe' } else { 'RitsukageGif' })
$smoke = & $executable --version | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Published application could not start.' }
$smokeReply = ($smoke -join "`n") | ConvertFrom-Json
if (!$smokeReply.Success -or $smokeReply.State -ne 'version' -or $smokeReply.Message -ne $versions.Application) { throw 'Published application command check failed.' }
Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Extension -in '.pdb', '.xml', '.dbg' } | Remove-Item -Force
$packageName = "RitsukageGif-$($versions.Application)-$RuntimeIdentifier"
if ($IsWindows) {
    $fileVersion = (Get-Item -LiteralPath (Join-Path $publish 'RitsukageGif.exe')).VersionInfo
    if ($fileVersion.FileVersion -ne $versions.NumericVersion -or $fileVersion.ProductVersion -ne $versions.Application) { throw 'Unexpected file version.' }
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $outputPath "$packageName.zip") -Force
}
elseif ($IsMacOS) {
    $swiftArchitecture = if ($architecture -eq 'arm64') { 'arm64' } else { 'x86_64' }
    $swiftSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'native/macos') -Filter '*.swift' -File | ForEach-Object FullName)
    & xcrun swiftc -swift-version 5 -O -parse-as-library -target "$swiftArchitecture-apple-macosx13.0" @swiftSources -o (Join-Path $publish 'RitsukageGif.Capture') -framework ScreenCaptureKit -framework CoreMedia -framework CoreVideo -framework CoreGraphics -framework Foundation
    if ($LASTEXITCODE -ne 0) { throw 'ScreenCaptureKit helper build failed.' }
    $permission = & (Join-Path $publish 'RitsukageGif.Capture') permission
    if ($LASTEXITCODE -notin 0, 77 -or ($permission -join '').Trim() -notin 'true', 'false') { throw 'ScreenCaptureKit helper could not start.' }
    $bundle = Join-Path $staging 'RitsukageGif.app'
    $contents = Join-Path $bundle 'Contents'
    $macos = Join-Path $contents 'MacOS'
    $resources = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null
    Get-ChildItem -LiteralPath $publish | Where-Object { $_.Name -notlike '*.dSYM' } | Copy-Item -Destination $macos -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'native/macos/Info.plist') -Destination $contents
    & /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $($versions.ShortVersion)" (Join-Path $contents 'Info.plist')
    if ($LASTEXITCODE -ne 0) { throw 'Application short version could not be set.' }
    & /usr/libexec/PlistBuddy -c "Set :CFBundleVersion $($versions.NumericVersion)" (Join-Path $contents 'Info.plist')
    if ($LASTEXITCODE -ne 0) { throw 'Application bundle version could not be set.' }
    foreach ($language in 'en', 'zh-Hans') {
        $localized = Join-Path $resources "$language.lproj"
        New-Item -ItemType Directory -Path $localized | Out-Null
        Copy-Item -LiteralPath (Join-Path $root "native/macos/$language.lproj/InfoPlist.strings") -Destination $localized
    }
    & codesign --force --deep --sign - $bundle
    if ($LASTEXITCODE -ne 0) { throw 'Application ad-hoc signing failed.' }
    & codesign --verify --deep --strict $bundle
    if ($LASTEXITCODE -ne 0) { throw 'Application signature verification failed.' }
    & ditto -c -k --sequesterRsrc --keepParent $bundle (Join-Path $outputPath "$packageName.zip")
    if ($LASTEXITCODE -ne 0) { throw 'Application archive failed.' }
}
else {
    $nativeOutput = & pkg-config --cflags --libs gio-2.0 gio-unix-2.0 gstreamer-app-1.0 gstreamer-video-1.0
    if ($LASTEXITCODE -ne 0) { throw 'Portal capture build dependencies are missing.' }
    $nativeFlags = @(($nativeOutput -join ' ') -split '\s+' | Where-Object { $_ })
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/portal-capture.c') -o (Join-Path $publish 'RitsukageGif.Capture') @nativeFlags
    if ($LASTEXITCODE -ne 0) { throw 'Portal capture helper build failed.' }
    & tar -czf (Join-Path $outputPath "$packageName.tar.gz") -C $publish .
    if ($LASTEXITCODE -ne 0) { throw 'Application archive failed.' }
    $deb = Join-Path $staging 'deb'
    $application = Join-Path $deb 'opt/ritsukagegif'
    $bin = Join-Path $deb 'usr/bin'
    $desktop = Join-Path $deb 'usr/share/applications'
    $control = Join-Path $deb 'DEBIAN'
    New-Item -ItemType Directory -Force -Path $application, $bin, $desktop, $control | Out-Null
    Get-ChildItem -LiteralPath $publish | Copy-Item -Destination $application -Recurse
    $debArchitecture = if ($architecture -eq 'arm64') { 'arm64' } else { 'amd64' }
    @"
Package: ritsukagegif
Version: $($versions.DebianVersion)
Architecture: $debArchitecture
Maintainer: OLC
Section: graphics
Priority: optional
Depends: libc6 (>= 2.38), libgcc-s1, libstdc++6, zlib1g, libicu74, libssl3t64, libfontconfig1, libfreetype6, libx11-6, libxext6, libxcomposite1, libxfixes3, libxrandr2, libice6, libsm6, libgtk-3-0t64, libglib2.0-0t64, libgstreamer1.0-0, libgstreamer-plugins-base1.0-0, gstreamer1.0-plugins-base, gstreamer1.0-pipewire, xdg-desktop-portal
Description: Screen and window animation recorder
"@ | Set-Content -LiteralPath (Join-Path $control 'control') -Encoding utf8NoBOM
    @'
#!/bin/sh
exec /opt/ritsukagegif/RitsukageGif "$@"
'@ | Set-Content -LiteralPath (Join-Path $bin 'ritsukagegif') -Encoding utf8NoBOM
    @'
[Desktop Entry]
Type=Application
Name=RitsukageGif
Exec=/opt/ritsukagegif/RitsukageGif
Terminal=false
Categories=Graphics;Recorder;
StartupWMClass=RitsukageGif
'@ | Set-Content -LiteralPath (Join-Path $desktop 'ritsukagegif.desktop') -Encoding utf8NoBOM
    & chmod 755 (Join-Path $bin 'ritsukagegif') (Join-Path $application 'RitsukageGif') (Join-Path $application 'RitsukageGif.Capture')
    if ($LASTEXITCODE -ne 0) { throw 'Application executable permissions could not be set.' }
    & dpkg-deb --root-owner-group --build $deb (Join-Path $outputPath "$packageName-ubuntu24.04.deb")
    if ($LASTEXITCODE -ne 0) { throw 'Debian package build failed.' }
}
