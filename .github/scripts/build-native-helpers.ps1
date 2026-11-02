param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-arm64')]
    [string]$RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$hostPlatform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if ($RuntimeIdentifier -ne "$hostPlatform-$architecture") { throw 'Native helpers require a matching build host.' }
$artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$publish = [IO.Path]::GetFullPath((Join-Path $artifacts "native-helpers-$RuntimeIdentifier"))
if (!$publish.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { throw 'Invalid native helper output directory.' }
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publish | Out-Null
if ($IsMacOS) {
    $env:MACOSX_DEPLOYMENT_TARGET = '13.0'
    $swiftArchitecture = if ($architecture -eq 'arm64') { 'arm64' } else { 'x86_64' }
    $cursorObject = Join-Path $artifacts "cursor-visibility-$RuntimeIdentifier.o"
    & xcrun clang -std=c11 -Os -Wall -Wextra -Werror -target "$swiftArchitecture-apple-macosx13.0" -c (Join-Path $root 'native/macos/CursorVisibility.c') -o $cursorObject
    if ($LASTEXITCODE -ne 0) { throw 'Cursor visibility bridge build failed.' }
    $swiftSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'native/macos') -Filter '*.swift' -File | Where-Object Name -NE 'foreground-policy.swift' | ForEach-Object FullName)
    & xcrun swiftc -swift-version 5 -O -parse-as-library -target "$swiftArchitecture-apple-macosx13.0" @swiftSources $cursorObject -import-objc-header (Join-Path $root 'native/macos/CursorVisibility.h') -o (Join-Path $publish 'RitsukageGif.Capture') -framework ScreenCaptureKit -framework CoreMedia -framework CoreVideo -framework CoreGraphics -framework CoreImage -framework AppKit -framework ApplicationServices -framework Foundation
    if ($LASTEXITCODE -ne 0) { throw 'ScreenCaptureKit helper build failed.' }
    $printSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'native/macos/print') -Filter '*.swift' -File | ForEach-Object FullName)
    & xcrun --sdk macosx swiftc -swift-version 5 -O -parse-as-library -target "$swiftArchitecture-apple-macosx13.0" @printSources -o (Join-Path $publish 'RitsukageGif.Print') -framework Foundation -framework AppKit -framework PDFKit -framework ApplicationServices -framework CoreGraphics
    if ($LASTEXITCODE -ne 0) { throw 'Native print helper build failed.' }
    & bash (Join-Path $root 'native/macos/hdr-preview/build.sh') $swiftArchitecture $publish
    if ($LASTEXITCODE -ne 0) { throw 'Metal HDR preview library build failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $publish 'libRitsukageHdrPreview.dylib')) -or
        !(Test-Path -LiteralPath (Join-Path $publish 'RitsukageHdrPreview.metallib'))) {
        throw 'Metal HDR preview output is missing.'
    }
    Remove-Item -LiteralPath (Join-Path $publish 'HdrPreview.air')
    $audioSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'native/macos/audio') -Filter '*.swift' -File | ForEach-Object FullName)
    & xcrun swiftc -swift-version 5 -O -parse-as-library -target "$swiftArchitecture-apple-macosx13.0" @audioSources -o (Join-Path $publish 'RitsukageGif.Audio') -framework AVFoundation -framework ScreenCaptureKit -framework CoreMedia -framework AudioToolbox -framework Foundation
    if ($LASTEXITCODE -ne 0) { throw 'Audio helper build failed.' }
    $audioVersion = & (Join-Path $publish 'RitsukageGif.Audio') version
    if ($LASTEXITCODE -ne 0 -or ($audioVersion -join '').Trim() -ne '1') { throw 'Audio helper could not start.' }
    $ocrSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'native/macos/ocr') -Filter '*.swift' -File | ForEach-Object FullName)
    & xcrun swiftc -swift-version 5 -O -parse-as-library -target "$swiftArchitecture-apple-macosx13.0" @ocrSources -o (Join-Path $publish 'RitsukageGif.Ocr') -framework Vision -framework CoreGraphics -framework Foundation
    if ($LASTEXITCODE -ne 0) { throw 'Vision OCR helper build failed.' }
    $ocrCapabilities = & (Join-Path $publish 'RitsukageGif.Ocr') capabilities
    if ($LASTEXITCODE -ne 0) { throw 'Vision OCR helper could not start.' }
    $ocrInfo = ($ocrCapabilities -join '') | ConvertFrom-Json
    if ($ocrInfo.maxImageDimension -le 0 -or $null -eq $ocrInfo.languages) { throw 'Vision OCR helper returned invalid capabilities.' }
    $permission = & (Join-Path $publish 'RitsukageGif.Capture') permission
    if ($LASTEXITCODE -notin 0, 77 -or ($permission -join '').Trim() -notin 'true', 'false') { throw 'ScreenCaptureKit helper could not start.' }
    & xcrun clang -std=c11 -Os -Wall -Wextra -Werror -target "$swiftArchitecture-apple-macosx13.0" (Join-Path $root 'native/macos/login-launcher.c') -o (Join-Path $publish 'RitsukageGif.Login')
    if ($LASTEXITCODE -ne 0) { throw 'Login launcher build failed.' }
    & xcrun clang -dynamiclib -fobjc-arc -Os -Wall -Wextra -Werror -target "$swiftArchitecture-apple-macosx13.0" -framework AppKit -framework Foundation (Join-Path $root 'native/macos/tray.m') -o (Join-Path $publish 'libritsukagegif-tray.dylib')
    if ($LASTEXITCODE -ne 0) { throw 'Native tray library build failed.' }
    & xcrun swiftc -swift-version 5 -O -parse-as-library -target "$swiftArchitecture-apple-macosx13.0" (Join-Path $root 'native/macos/foreground-policy.swift') -o (Join-Path $publish 'RitsukageGif.Foreground') -framework AppKit -framework ApplicationServices -framework CoreGraphics -framework Foundation
    if ($LASTEXITCODE -ne 0) { throw 'Foreground policy helper build failed.' }
} elseif ($IsLinux) {
    $nativeOutput = & pkg-config --cflags --libs gio-2.0 gio-unix-2.0 json-glib-1.0 libpipewire-0.3
    if ($LASTEXITCODE -ne 0) { throw 'Portal capture build dependencies are missing.' }
    $nativeFlags = @(($nativeOutput -join ' ') -split '\s+' | Where-Object { $_ })
    $captureSources = @('portal-capture.c', 'portal-frame.c', 'portal-video.c', 'portal-scroll-input.c') | ForEach-Object { Join-Path $root "native/linux/$_" }
    & cc -std=c11 -D_GNU_SOURCE -O2 -Wall -Wextra -Werror @captureSources -o (Join-Path $publish 'RitsukageGif.Capture') @nativeFlags -lm
    if ($LASTEXITCODE -ne 0) { throw 'Portal capture helper build failed.' }
    $accessibilityOutput = & pkg-config --cflags --libs atspi-2 gobject-2.0 x11
    if ($LASTEXITCODE -ne 0) { throw 'Accessibility build dependencies are missing.' }
    $accessibilityFlags = @(($accessibilityOutput -join ' ') -split '\s+' | Where-Object { $_ })
    & cc -std=c11 -D_GNU_SOURCE -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/accessibility-hit-test.c') -o (Join-Path $publish 'RitsukageGif.Accessibility') @accessibilityFlags -lm
    if ($LASTEXITCODE -ne 0) { throw 'Accessibility helper build failed.' }
    $shortcutFlagsOutput = & pkg-config --cflags --libs gio-2.0
    if ($LASTEXITCODE -ne 0) { throw 'Portal shortcuts build dependencies are missing.' }
    $shortcutFlags = @(($shortcutFlagsOutput -join ' ') -split '\s+' | Where-Object { $_ })
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/portal-shortcuts.c') -o (Join-Path $publish 'RitsukageGif.Shortcuts') @shortcutFlags
    if ($LASTEXITCODE -ne 0) { throw 'Portal shortcuts helper build failed.' }
    $observerFlagsOutput = & pkg-config --cflags --libs x11 glib-2.0
    if ($LASTEXITCODE -ne 0) { throw 'Global input build dependencies are missing.' }
    $observerFlags = @(($observerFlagsOutput -join ' ') -split '\s+' | Where-Object { $_ })
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/x11-shortcuts.c') -o (Join-Path $publish 'RitsukageGif.X11Shortcuts') @observerFlags -lXfixes
    if ($LASTEXITCODE -ne 0) { throw 'X11 shortcuts helper build failed.' }
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/foreground-policy.c') -o (Join-Path $publish 'RitsukageGif.Foreground') @observerFlags
    if ($LASTEXITCODE -ne 0) { throw 'Foreground policy helper build failed.' }
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/mouse-observer.c') -o (Join-Path $publish 'RitsukageGif.MouseObserver') -lXtst -lX11 -lpthread
    if ($LASTEXITCODE -ne 0) { throw 'Global mouse helper build failed.' }
    $printNativeOutput = & pkg-config --cflags --libs gio-2.0 gio-unix-2.0
    if ($LASTEXITCODE -ne 0) { throw 'Portal print build dependencies are missing.' }
    $printNativeFlags = @(($printNativeOutput -join ' ') -split '\s+' | Where-Object { $_ })
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/portal-print.c') -o (Join-Path $publish 'RitsukageGif.Print') @printNativeFlags -lm
    if ($LASTEXITCODE -ne 0) { throw 'Portal print helper build failed.' }
    $trayFlagsOutput = & pkg-config --cflags --libs gio-unix-2.0
    if ($LASTEXITCODE -ne 0) { throw 'Native tray build dependencies are missing.' }
    $trayFlags = @(($trayFlagsOutput -join ' ') -split '\s+' | Where-Object { $_ })
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $root 'native/linux/tray-helper.c') -o (Join-Path $publish 'ritsukagegif-tray') @trayFlags
    if ($LASTEXITCODE -ne 0) { throw 'Native tray helper build failed.' }
}
$files = @(Get-ChildItem -LiteralPath $publish -File | ForEach-Object Name)
@{ RuntimeIdentifier = $RuntimeIdentifier; Files = $files } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publish 'native-helpers.json') -Encoding utf8NoBOM
