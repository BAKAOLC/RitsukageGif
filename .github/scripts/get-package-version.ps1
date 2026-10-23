param([string]$Tag = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$properties = ([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup
$version = [string]$properties.Version
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Application version must contain four numeric components.' }
foreach ($property in 'AssemblyVersion', 'FileVersion', 'InformationalVersion') {
    if ([string]$properties.$property -ne $version) { throw "$property must match Version ($version)." }
}
$extension = Get-Content -LiteralPath (Join-Path $root 'extensions/vscode/package.json') -Raw | ConvertFrom-Json
if ($extension.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Extension version must contain three numeric components.' }
if ($Tag -and $Tag -cne "v$version") { throw "Tag '$Tag' must match project version 'v$version'." }
[pscustomobject]@{
    Application = $version
    Extension = [string]$extension.version
    ShortVersion = ([version]$version).ToString(3)
}
