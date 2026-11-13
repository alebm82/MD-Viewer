<#
.SYNOPSIS
    Downloads the bundled Google Fonts (WOFF) and writes the manifest that
    src\Fonts.cs reads at runtime.

.DESCRIPTION
    MD Viewer renders its preview with the IE11 engine that ships with
    Windows 10. That engine understands WOFF but not WOFF2, so the script
    asks Google Fonts with an IE11 user agent, which makes the service
    answer with .woff files.

    The fonts land in assets\fonts and are embedded into MDViewer.exe by
    build.bat, so the viewer works offline.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fetch-fonts.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($OutDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Join-Path (Get-Location) "tools" }
    $OutDir = Join-Path (Split-Path -Parent $scriptDir) "assets\fonts"
}

# The engine inside the WebBrowser control is MSHTML (IE11). Requesting the
# page as IE11 makes Google Fonts serve WOFF, which MSHTML can render.
$UserAgent = "Mozilla/5.0 (Windows NT 6.1; Trident/7.0; rv:11.0) like Gecko"

# id | display name | category | arabic | faces (style:google-weight)
$Catalog = @(
    @{ Id = "ebgaramond";            Name = "EB Garamond";            Category = "Serif"; Arabic = $false; Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }, @{ Style = "italic"; Weight = "400" }) },
    @{ Id = "sourceserif";           Name = "Source Serif 4";         Category = "Serif"; Arabic = $false; Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "notonaskharabic";       Name = "Noto Naskh Arabic";      Category = "Serif"; Arabic = $true;  Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "amiri";                 Name = "Amiri";                  Category = "Serif"; Arabic = $true;  Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "inter";                 Name = "Inter";                  Category = "Sans";  Arabic = $false; Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "ibmplexsans";          Name = "IBM Plex Sans";          Category = "Sans";  Arabic = $false; Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "ibmplexsansarabic";    Name = "IBM Plex Sans Arabic";  Category = "Sans";  Arabic = $true;  Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "cairo";                 Name = "Cairo";                  Category = "Sans";  Arabic = $true;  Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "tajawal";               Name = "Tajawal";                Category = "Sans";  Arabic = $true;  Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "notosansarabic";        Name = "Noto Sans Arabic";       Category = "Sans";  Arabic = $true;  Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "jetbrainsmono";         Name = "JetBrains Mono";         Category = "Mono";  Arabic = $false; Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) },
    @{ Id = "ibmplexmono";          Name = "IBM Plex Mono";          Category = "Mono";  Arabic = $false; Faces = @(@{ Style = "regular"; Weight = "400" }, @{ Style = "bold"; Weight = "700" }) }
)

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

function Get-FontCss {
    param([string]$Family, [string]$Weights)

    $url = "https://fonts.googleapis.com/css?family={0}:{1}" -f [uri]::EscapeDataString($Family), $Weights
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            return (Invoke-WebRequest -Uri $url -UserAgent $UserAgent -UseBasicParsing -TimeoutSec 30).Content
        }
        catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
}

function Save-FontFile {
    param([string]$Url, [string]$Target)

    $temp = $Target + ".part"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $temp -UseBasicParsing -TimeoutSec 60
            break
        }
        catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
    Move-Item -Force $temp $Target
}

$manifest = New-Object System.Collections.Generic.List[string]
$total = 0

foreach ($family in $Catalog) {
    # Google asks for italics as "<weight>italic", e.g. "400italic".
    $weights = ($family.Faces | ForEach-Object {
        if ($_.Style -eq "italic") { $_.Weight + "italic" }
        elseif ($_.Style -eq "bolditalic") { $_.Weight + "italic" }
        else { $_.Weight }
    }) -join ","
    Write-Host ("[fonts] {0} ({1})" -f $family.Name, $family.Category)

    $css = Get-FontCss -Family $family.Name -Weights $weights
    $blocks = [regex]::Matches($css, "@font-face\s*\{[^}]*\}")

    $faces = New-Object System.Collections.Generic.List[string]

    foreach ($face in $family.Faces) {
        $style = $face.Style
        $weight = $face.Weight

        # Match the block that carries this weight and style.
        $chosen = $null
        foreach ($block in $blocks) {
            $text = $block.Value
            $blockWeight = ([regex]::Match($text, "font-weight:\s*([0-9]+)")).Groups[1].Value
            $blockStyle = ([regex]::Match($text, "font-style:\s*([a-z]+)")).Groups[1].Value
            if ([string]::IsNullOrEmpty($blockStyle)) { $blockStyle = "normal" }

            $isItalic = ($style -eq "italic" -or $style -eq "bolditalic")
            if ($blockWeight -eq $weight -and $blockStyle -eq $(if ($isItalic) { "italic" } else { "normal" })) {
                $chosen = $text
                break
            }
        }

        if ($chosen -eq $null) {
            Write-Warning ("  missing face {0} {1}" -f $weight, $style)
            continue
        }

        $url = ([regex]::Match($chosen, "url\((https://fonts\.gstatic\.com/[^)]+)\)")).Groups[1].Value
        if ([string]::IsNullOrEmpty($url)) {
            Write-Warning ("  missing url for {0} {1}" -f $weight, $style)
            continue
        }

        $file = "{0}-{1}.woff" -f $family.Id, $style
        $target = Join-Path $OutDir $file
        Save-FontFile -Url $url -Target $target

        $size = (Get-Item $target).Length
        $total += $size
        Write-Host ("  {0,-10} {1,8:N0} bytes" -f $style, $size)

        $faces.Add(("{0}:{1}" -f $style, $file))
    }

    $line = "{0}|{1}|{2}|{3}|{4}" -f `
        $family.Id, $family.Name, $family.Category, $(if ($family.Arabic) { "arabic" } else { "latin" }), ($faces -join ",")
    $manifest.Add($line)
}

$manifestPath = Join-Path $OutDir "manifest.txt"
$header = @(
    "# MD Viewer font manifest",
    "# id|display name|category|script|face:file,face:file",
    "# Generated by tools\fetch-fonts.ps1 - do not edit by hand."
)

# Write through a temporary file: a scanner can hold the manifest open for a
# moment right after it was created, and a plain overwrite would fail.
$tempManifest = $manifestPath + ".tmp"
($header + $manifest) | Set-Content -Path $tempManifest -Encoding UTF8
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        if (Test-Path $manifestPath) { Remove-Item -Force $manifestPath -ErrorAction SilentlyContinue }
        Move-Item -Force $tempManifest $manifestPath
        break
    }
    catch {
        if ($attempt -eq 10) {
            # Last resort: write straight to the destination.
            Copy-Item -Force $tempManifest $manifestPath
            Remove-Item -Force $tempManifest -ErrorAction SilentlyContinue
            break
        }
        Start-Sleep -Seconds 1
    }
}
Remove-Item -Force $tempManifest -ErrorAction SilentlyContinue

Write-Host ""
Write-Host ("[fonts] {0} families, {1:N0} bytes total" -f $Catalog.Count, $total)
Write-Host ("[fonts] manifest -> {0}" -f $manifestPath)
