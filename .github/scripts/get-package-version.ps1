param(
    [string]$Tag = '',
    [switch]$Development,
    [string]$RunNumber = $env:GITHUB_RUN_NUMBER,
    [string]$RunAttempt = $env:GITHUB_RUN_ATTEMPT,
    [string]$Commit = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$properties = @(([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup)[0]
$version = [string]$properties.Version
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Application version must contain four numeric components.' }
if (@($version.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count) { throw 'Numeric version components must fit Windows version fields.' }
foreach ($property in 'AssemblyVersion', 'FileVersion', 'InformationalVersion') {
    if ([string]$properties.$property -ne $version) { throw "$property must match Version ($version)." }
}
$extension = Get-Content -LiteralPath (Join-Path $root 'extensions/vscode/package.json') -Raw | ConvertFrom-Json
if ($extension.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Extension version must contain three numeric components.' }
if ($Tag -and $Tag -cne "v$version") { throw "Tag '$Tag' must match project version 'v$version'." }
if ($Tag -and $Development) { throw 'A release tag cannot be a development build.' }
$application = $version
if ($Development) {
    if ($RunNumber -notmatch '^[1-9]\d*$' -or $RunAttempt -notmatch '^[1-9]\d*$') { throw 'Development builds require positive run and attempt numbers.' }
    if (!$Commit) { $Commit = & git -C $root rev-parse HEAD; if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve build commit.' } }
    if ($Commit -notmatch '^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$') { throw 'Invalid build commit.' }
    $application = "$version-dev.$RunNumber.$RunAttempt+$($Commit.Substring(0, 12).ToLowerInvariant())"
}
elseif ($env:RG_BUILD_VERSION) {
    $application = $env:RG_BUILD_VERSION
    if ($application -ne $version -and $application -notmatch "^$([regex]::Escape($version))-dev\.[1-9]\d*\.[1-9]\d*\+[0-9a-f]{12}$") { throw 'Build version does not match the project version.' }
    if ($Tag -and $application -ne $version) { throw 'A release tag must use the exact release version.' }
}
$suffix = $application.Substring($version.Length)
[pscustomobject]@{
    Application = $application
    NumericVersion = $version
    DebianVersion = $application.Replace('-dev.', '~dev.')
    Extension = [string]$extension.version
    ExtensionPackage = "$($extension.version)$suffix"
    Development = $application -ne $version
    ShortVersion = ([version]$version).ToString(3)
}
