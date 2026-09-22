param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = 'Stop'

function Get-OpenHoYoShadeVersion {
    param([string]$BaseName)

    $match = [regex]::Match($BaseName, '(?i)^OpenHoYoShade[-_ ]*v?(\d+(?:\.\d+){0,3})(?:[-_ ](.+))?$')
    if (-not $match.Success) {
        return $null
    }

    $numbers = @($match.Groups[1].Value.Split('.') | ForEach-Object { [int]$_ })
    while ($numbers.Count -lt 4) {
        $numbers += 0
    }

    $preRelease = $match.Groups[2].Value
    $stage = 3
    if ($preRelease -match '(?i)^(alpha|a)') {
        $stage = 0
    }
    elseif ($preRelease -match '(?i)^(beta|b)') {
        $stage = 1
    }
    elseif ($preRelease -match '(?i)^(rc)') {
        $stage = 2
    }

    $stageNumber = 0
    if ($preRelease -match '(\d+)') {
        $stageNumber = [int]$matches[1]
    }

    [PSCustomObject]@{
        Major = $numbers[0]
        Minor = $numbers[1]
        Patch = $numbers[2]
        Revision = $numbers[3]
        Stage = $stage
        StageNumber = $stageNumber
        Version = if ([string]::IsNullOrWhiteSpace($preRelease)) { $match.Groups[1].Value } else { "$($match.Groups[1].Value)-$preRelease" }
    }
}

$archives = @(
    Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.zip' -File |
        Where-Object { $_.BaseName -match '(?i)openhoyoshade' } |
        ForEach-Object {
            $version = Get-OpenHoYoShadeVersion $_.BaseName
            if ($null -ne $version) {
                $version | Add-Member -NotePropertyName Archive -NotePropertyValue $_ -PassThru
            }
        }
)

if ($archives.Count -eq 0) {
    throw "No OpenHoYoShade package was found in '$PackageDirectory'."
}

$selected = $archives |
    Sort-Object Major, Minor, Patch, Revision, Stage, StageNumber -Descending |
    Select-Object -First 1

$archive = $selected.Archive
$signature = "$($archive.Name)|$($archive.Length)|$($archive.LastWriteTimeUtc.Ticks)"
$markerPath = Join-Path $DestinationDirectory '.source'
$requiredFiles = @(
    'ReShade64.dll',
    'LICENSE',
    'ReShade_LICENSE',
    'Runtime.version'
)
$isPrepared = (Test-Path -LiteralPath $markerPath) -and ((Get-Content -LiteralPath $markerPath -Raw) -eq $signature)
foreach ($requiredFile in $requiredFiles) {
    $isPrepared = $isPrepared -and (Test-Path -LiteralPath (Join-Path $DestinationDirectory $requiredFile))
}

if ($isPrepared) {
    exit 0
}

if (Test-Path -LiteralPath $DestinationDirectory) {
    Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
}

$extractDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("OpenHoYoShade-" + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $archive.FullName -DestinationPath $extractDirectory -Force
    $library = Get-ChildItem -LiteralPath $extractDirectory -Filter 'ReShade64.dll' -File -Recurse | Select-Object -First 1
    if ($null -eq $library) {
        throw "OpenHoYoShade package '$($archive.Name)' does not contain ReShade64.dll."
    }

    $sourceDirectory = $library.Directory.FullName
    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    foreach ($relativeFile in @('ReShade64.dll', 'LICENSE', 'ReShade_LICENSE')) {
        $sourcePath = Join-Path $sourceDirectory $relativeFile
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            throw "OpenHoYoShade package '$($archive.Name)' is missing '$relativeFile'."
        }

        $destinationPath = Join-Path $DestinationDirectory $relativeFile
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }

    $launcherDirectory = Join-Path $DestinationDirectory 'LauncherResource'
    New-Item -ItemType Directory -Path $launcherDirectory -Force | Out-Null
    foreach ($relativeFile in @('LauncherResource\INIBuild.exe', 'LauncherResource\AddonWhitelist.txt')) {
        $sourcePath = Join-Path $sourceDirectory $relativeFile
        if (-not (Test-Path -LiteralPath $sourcePath)) {
            throw "OpenHoYoShade package '$($archive.Name)' is missing '$relativeFile'."
        }

        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $DestinationDirectory $relativeFile) -Force
    }

    foreach ($relativeDirectory in @('InjectResource', 'Presets')) {
        $sourcePath = Join-Path $sourceDirectory $relativeDirectory
        if (Test-Path -LiteralPath $sourcePath) {
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $DestinationDirectory $relativeDirectory) -Recurse -Force
        }
    }

    Set-Content -LiteralPath (Join-Path $DestinationDirectory 'Runtime.version') -Value $selected.Version -NoNewline
    Set-Content -LiteralPath $markerPath -Value $signature -NoNewline
}
finally {
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
}
