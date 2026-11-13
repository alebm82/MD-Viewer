<#
.SYNOPSIS
    Builds the Windows Installer package for MD Viewer.

.DESCRIPTION
    Compiles the program with build.bat and then wraps it into an MSI with
    the WiX toolset.

    The package carries a fixed UpgradeCode and the version of the program, so
    a newer package replaces an installed one in place: the old product is
    removed by the installer itself and the user never has to uninstall
    anything first. The settings, the draft and the unpacked fonts live in
    %LOCALAPPDATA%\MD Viewer and survive the upgrade.

.PARAMETER Version
    The version of the package. Defaults to the version in
    src\AssemblyInfo.cs.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDir,
    [switch]$SkipCompile
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = Split-Path -Parent (Get-Location) }

function Get-ProgramVersion {
    $info = Join-Path $root "src\AssemblyInfo.cs"
    if (-not (Test-Path $info)) { return $null }

    $text = Get-Content $info -Raw
    $match = [regex]::Match($text, 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)')
    if (-not $match.Success) { return $null }

    # Windows Installer compares the first three fields.
    return "{0}.{1}.{2}" -f $match.Groups[1].Value, $match.Groups[2].Value, $match.Groups[3].Value
}

if ([string]::IsNullOrEmpty($Version)) {
    $Version = Get-ProgramVersion
    if ([string]::IsNullOrEmpty($Version)) {
        throw "Could not read the version from src\AssemblyInfo.cs - pass -Version."
    }
}

if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $root "bin"
}
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null }

# --- the program itself -----------------------------------------------------
if (-not $SkipCompile) {
    Write-Host "[msi] building the program"
    & cmd /c (Join-Path $root "build.bat")
    if ($LASTEXITCODE -ne 0) { throw "build.bat failed" }
}

$exe = Join-Path $root "bin\MDViewer.exe"
if (-not (Test-Path $exe)) { throw "bin\MDViewer.exe is missing" }

# --- the WiX toolset --------------------------------------------------------
# WiX 5 is used on purpose: it is the last release under the MPL-2.0 licence
# and needs no licence acceptance. Newer releases ask for the Open Source
# Maintenance Fee to be confirmed once.
$wixVersion = "5.0.2"

$wix = Get-Command wix -ErrorAction SilentlyContinue
if ($wix -eq $null) {
    $tool = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
    if (Test-Path $tool) { $wix = Get-Item $tool }
}
if ($wix -eq $null) {
    Write-Host "[msi] installing the WiX toolset"
    & dotnet tool install --global wix --version $wixVersion
    if ($LASTEXITCODE -ne 0) { throw "the WiX toolset could not be installed" }
    $tool = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
    if (-not (Test-Path $tool)) { throw "wix.exe was not found after the installation" }
    $wix = Get-Item $tool
}

# --- the user interface of the installer -------------------------------------
# The dialogs live in a separate extension that has to match the toolset.
$extensionFolder = Join-Path $root "installer\.wix"
$extension = Join-Path $extensionFolder "WixToolset.UI.wixext.dll"

if (-not (Test-Path $extension)) {
    if (-not (Test-Path $extensionFolder)) {
        New-Item -ItemType Directory -Force -Path $extensionFolder | Out-Null
    }

    $toolVersion = (& $wix.FullName --version).ToString().Split("+")[0]
    $package = "https://api.nuget.org/v3-flatcontainer/wixtoolset.ui.wixext/{0}/wixtoolset.ui.wixext.{0}.nupkg" -f $toolVersion
    $packageFile = Join-Path $extensionFolder "wixui.nupkg"

    Write-Host ("[msi] fetching the installer dialogs ({0})" -f $toolVersion)
    try {
        Invoke-WebRequest -Uri $package -OutFile $packageFile -UseBasicParsing -TimeoutSec 180

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($packageFile)
        try {
            $entry = $zip.Entries | Where-Object { $_.FullName -like "wixext*/*.wixext.dll" } | Select-Object -First 1
            if ($entry -eq $null) { throw "the package does not contain the extension" }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $extension, $true)
        }
        finally {
            $zip.Dispose()
        }
        Remove-Item $packageFile -Force -ErrorAction SilentlyContinue
    }
    catch {
        throw "the installer dialogs could not be downloaded: $($_.Exception.Message)"
    }
}

$output = Join-Path $OutputDir ("MDViewer-{0}-x64.msi" -f $Version)
if (Test-Path $output) { Remove-Item $output -Force }

# Forward slashes keep the paths safe from the quoting rules of the command
# line, which would otherwise split a folder that carries a space.
$projectDir = ($root -replace '\\', '/') + '/'
$installerDir = ((Join-Path $root "installer") -replace '\\', '/') + '/'
$source = (Join-Path $root "installer\main.wxs") -replace '\\', '/'
$outputPath = $output -replace '\\', '/'

Write-Host ("[msi] package version {0}" -f $Version)
Write-Host ("[msi] output {0}" -f $output)

& $wix.FullName build $source `
    -arch x64 `
    -ext $extension `
    -d "ProductVersion=$Version" `
    -d "ProjectDir=$projectDir" `
    -d "InstallerDir=$installerDir" `
    -o $outputPath

if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

# The symbol file is only useful while debugging the package.
$symbols = Join-Path $OutputDir ("MDViewer-{0}-x64.wixpdb" -f $Version)
Remove-Item $symbols -Force -ErrorAction SilentlyContinue

$size = [Math]::Round((Get-Item $output).Length / 1KB, 1)
Write-Host ("[msi] done - {0} ({1} KB)" -f $output, $size)
