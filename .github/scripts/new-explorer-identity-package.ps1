param(
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$StagingDirectory
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Identity package creation requires the Windows SDK.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$staging = [IO.Path]::GetFullPath($StagingDirectory)
$outputPath = [IO.Path]::GetFullPath($Output)
$manifestPath = Join-Path $staging 'AppxManifest.xml'
if (Test-Path -LiteralPath $manifestPath) { throw 'Use an empty identity package staging directory.' }
$assets = Join-Path $staging 'Assets'
New-Item -ItemType Directory -Force -Path $assets, ([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$template = [IO.File]::ReadAllText((Join-Path $root 'native/windows/explorer/AppxManifest.xml.in'))
$manifest = $template.Replace('@PUBLISHER@', [Security.SecurityElement]::Escape($Publisher))
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
$applicationManifest = [xml][IO.File]::ReadAllText((Join-Path $root 'src/RitsukageGif/app.manifest'))
$identity = $applicationManifest.CreateElement('msix', 'urn:schemas-microsoft-com:msix.v1')
$identity.SetAttribute('publisher', $Publisher)
$identity.SetAttribute('packageName', 'OLC.RitsukageGif')
$identity.SetAttribute('applicationId', 'RitsukageGif')
$applicationManifest.DocumentElement.AppendChild($identity) | Out-Null
$identityManifestPath = [IO.Path]::ChangeExtension($outputPath, '.manifest')
$applicationManifest.Save($identityManifestPath)
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::new((Join-Path $root 'src/RitsukageGif/Icon.ico'))
try {
    $source = $icon.ToBitmap()
    try {
        foreach ($entry in @(@('Logo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
            $bitmap = [Drawing.Bitmap]::new($source, [Drawing.Size]::new($entry[1], $entry[1]))
            try { $bitmap.Save((Join-Path $assets $entry[0]), [Drawing.Imaging.ImageFormat]::Png) }
            finally { $bitmap.Dispose() }
        }
    }
    finally { $source.Dispose() }
}
finally { $icon.Dispose() }
$sdk = Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Windows Kits/10/bin' -Directory |
    Where-Object Name -Match '^10\.' | Sort-Object { [version]$_.Name } -Descending |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'x64/makeappx.exe') } | Select-Object -First 1
if (!$sdk) { throw 'Windows SDK MakeAppx was not found.' }
& (Join-Path $sdk.FullName 'x64/makeappx.exe') pack /d $staging /p $outputPath /o /nv
if ($LASTEXITCODE -ne 0) { throw 'Identity manifest validation or unsigned package creation failed.' }
