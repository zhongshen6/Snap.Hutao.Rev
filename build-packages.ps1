param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$outputDir = Join-Path $repoRoot "output"
$installerProject = Join-Path $repoRoot "src\Snap.Hutao\Snap.Hutao.Installer\Snap.Hutao.Installer.wixproj"

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "==> Build MSI"
& dotnet build $installerProject -c $Configuration

$msiSearchRoot = Join-Path $repoRoot "src\Snap.Hutao\Snap.Hutao.Installer\bin\$Platform\$Configuration"
$msiFiles = Get-ChildItem -Path $msiSearchRoot -Recurse -Filter "*.msi" | Sort-Object LastWriteTime -Descending
if (-not $msiFiles) {
    throw "MSI build completed but no .msi file found under: $msiSearchRoot"
}

Write-Host "==> Copy MSI files to output"
foreach ($msi in $msiFiles) {
    $locale = Split-Path $msi.DirectoryName -Leaf
    $destName = "Snap.Hutao.Installer.$locale.msi"
    Copy-Item -Path $msi.FullName -Destination (Join-Path $outputDir $destName) -Force
}

Write-Host ""
Write-Host "Done. Output files:"
Get-ChildItem -Path $outputDir -File | Sort-Object LastWriteTime -Descending |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
