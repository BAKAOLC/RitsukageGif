param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Packages
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$versions = & (Join-Path $PSScriptRoot 'get-package-version.ps1') -Tag $Tag
$packagesPath = [IO.Path]::GetFullPath($Packages, $root)
$expected = @(
    "RitsukageGif-$($versions.Application)-win-x64.zip"
    "RitsukageGif-$($versions.Application)-osx-arm64.zip"
    "RitsukageGif-$($versions.Application)-linux-x64.tar.gz"
    "RitsukageGif-$($versions.Application)-linux-arm64.tar.gz"
    "RitsukageGif-$($versions.Application)-linux-x64-ubuntu24.04.deb"
    "RitsukageGif-$($versions.Application)-linux-arm64-ubuntu24.04.deb"
    "rgscript-$($versions.Extension)-win32-x64.vsix"
    "rgscript-$($versions.Extension)-darwin-arm64.vsix"
    "rgscript-$($versions.Extension)-linux-x64.vsix"
    "rgscript-$($versions.Extension)-linux-arm64.vsix"
)
$files = @(Get-ChildItem -LiteralPath $packagesPath -File)
if ($files.Count -ne $expected.Count) { throw 'The release must contain exactly ten platform packages.' }
foreach ($name in $expected) {
    $file = $files | Where-Object Name -CEQ $name
    if (!$file -or $file.Length -eq 0) { throw "Missing or empty release package: $name" }
}
$about = Get-Content -LiteralPath (Join-Path $root 'src/RitsukageGif/About.md') -Raw
$heading = [regex]::Escape($versions.Application)
$notes = [regex]::Match($about, "(?ms)^###\s+$heading\s*\r?`n(?<notes>.*?)(?=^###\s|\z)").Groups['notes'].Value.Trim()
if (!$notes) { throw "About.md must contain release notes under '### $($versions.Application)'." }
$notesPath = [IO.Path]::GetTempFileName()
try {
    $notes | Set-Content -LiteralPath $notesPath -Encoding utf8NoBOM
    $assets = @($files.FullName)
    $existingJson = & gh release view $Tag --json isDraft,assets 2>$null
    if ($LASTEXITCODE -eq 0) {
        $existing = ($existingJson -join "`n") | ConvertFrom-Json
        if (!$existing.isDraft) { throw 'This tag already has a published release; it will not be overwritten.' }
        foreach ($asset in $existing.assets) {
            if ($asset.name -cnotin $expected) { throw "The existing draft contains an unexpected asset: $($asset.name)" }
        }
        & gh release upload $Tag @assets --clobber
        if ($LASTEXITCODE -ne 0) { throw 'Draft asset upload failed; rerun this job to retry.' }
        & gh release edit $Tag --draft=false --title "RitsukageGif $($versions.Application)" --notes-file $notesPath
        if ($LASTEXITCODE -ne 0) { throw 'Draft publication failed; rerun this job to retry.' }
    }
    else {
        & gh release create $Tag @assets --verify-tag --title "RitsukageGif $($versions.Application)" --notes-file $notesPath
        if ($LASTEXITCODE -ne 0) { throw 'Release creation failed; rerun this job to retry an interrupted draft upload.' }
    }
}
finally { [IO.File]::Delete($notesPath) }
