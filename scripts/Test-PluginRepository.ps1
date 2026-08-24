[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $RepositoryRoot 'src\InzoneBudsBattery\InzoneBudsBattery.csproj'
$repositoryPath = Join-Path $RepositoryRoot 'pluginmaster.json'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$entries = @(Get-Content -LiteralPath $repositoryPath -Raw | ConvertFrom-Json)

if ($entries.Count -ne 1) {
    throw "pluginmaster.json must contain exactly one plugin entry; found $($entries.Count)."
}

$entry = $entries[0]
$assemblyVersion = [string]$project.Project.PropertyGroup.AssemblyVersion
$internalName = [string]$project.Project.PropertyGroup.InternalName
$releaseVersion = $assemblyVersion -replace '\.0$', ''
$releaseTag = "v$releaseVersion"
$expectedDownloadUrl = "https://github.com/zxe-ll/InzoneBudsBattery/releases/download/$releaseTag/latest.zip"

if ($entry.InternalName -ne $internalName) {
    throw "InternalName mismatch: project=$internalName, pluginmaster=$($entry.InternalName)."
}

if ($entry.AssemblyVersion -ne $assemblyVersion) {
    throw "AssemblyVersion mismatch: project=$assemblyVersion, pluginmaster=$($entry.AssemblyVersion)."
}

foreach ($property in 'DownloadLinkInstall', 'DownloadLinkUpdate', 'DownloadLinkTesting') {
    if ($entry.$property -ne $expectedDownloadUrl) {
        throw "$property must be $expectedDownloadUrl"
    }
}

if ($entry.DalamudApiLevel -ne 15) {
    throw "DalamudApiLevel must be 15; found $($entry.DalamudApiLevel)."
}

if ($PackagePath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)

    try {
        $actualFiles = @($archive.Entries | ForEach-Object FullName | Sort-Object)
        $expectedFiles = @(
            "$internalName.deps.json"
            "$internalName.dll"
            "$internalName.json"
        ) | Sort-Object

        if (Compare-Object -ReferenceObject $expectedFiles -DifferenceObject $actualFiles) {
            throw "Package contents must be exactly: $($expectedFiles -join ', '). Actual: $($actualFiles -join ', ')."
        }

        $manifestEntry = $archive.GetEntry("$internalName.json")
        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try {
            $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
        }

        if ($manifest.InternalName -ne $internalName -or $manifest.AssemblyVersion -ne $assemblyVersion) {
            throw 'The manifest inside the package does not match the project metadata.'
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Host "Plugin repository metadata is valid for $internalName $assemblyVersion ($releaseTag)."
