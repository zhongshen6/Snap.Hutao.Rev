param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$Runtime = "win-x64",
    [string]$MsixPackageRoot = "AppPackages\\AutoBuild",
    [string]$MsixCertificatePassword = "RevMsix2026",
    [switch]$UnsignedMsix
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$outputDir = Join-Path $repoRoot "output"
$installerProject = Join-Path $repoRoot "src\\Snap.Hutao\\Snap.Hutao.Installer\\Snap.Hutao.Installer.wixproj"
$appProject = Join-Path $repoRoot "src\\Snap.Hutao\\Snap.Hutao\\Snap.Hutao.csproj"
$msixRootAbsolute = Join-Path (Join-Path $repoRoot "src\\Snap.Hutao\\Snap.Hutao") $MsixPackageRoot

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $msixRootAbsolute -Force | Out-Null

Write-Host "==> Build MSI"
& dotnet build $installerProject -c $Configuration

$msiSearchRoot = Join-Path $repoRoot "src\\Snap.Hutao\\Snap.Hutao.Installer\\bin\\$Platform\\$Configuration"
$msiFiles = Get-ChildItem -Path $msiSearchRoot -Recurse -Filter "*.msi" | Sort-Object LastWriteTime -Descending
if (-not $msiFiles) {
    throw "MSI build completed but no .msi file found under: $msiSearchRoot"
}

Write-Host "==> Build MSIX"
$publishArgs = @(
    "publish"
    $appProject
    "-c", $Configuration
    "-r", $Runtime
    "-p:Platform=$Platform"
    "-p:AppxPackage=true"
    "-p:WindowsPackageType=MSIX"
    "-p:GenerateAppxPackageOnBuild=true"
    "-p:AppxBundle=Never"
    "-p:AppxPackageDir=$MsixPackageRoot\\"
)

if ($UnsignedMsix) {
    $publishArgs += "-p:AppxPackageSigningEnabled=false"
} else {
    $publishArgs += "-p:PackageCertificatePassword=$MsixCertificatePassword"
}

Push-Location (Join-Path $repoRoot "src\\Snap.Hutao\\Snap.Hutao")
try {
    & dotnet @publishArgs
} finally {
    Pop-Location
}

$latestMsix = Get-ChildItem -Path $msixRootAbsolute -Recurse -Filter "*.msix" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $latestMsix) {
    throw "MSIX build completed but no .msix file found under: $msixRootAbsolute"
}

$msixPackageFolder = $latestMsix.Directory
$zipPath = Join-Path $outputDir "$($msixPackageFolder.Name).zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "==> Zip MSIX folder"
Compress-Archive -Path $msixPackageFolder.FullName -DestinationPath $zipPath -Force

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
