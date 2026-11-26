param(
    [Parameter(Mandatory)] [string] $Iscc,
    [string] $Release = (Join-Path $PSScriptRoot '../Builds/RTS-0.1.0-Windows-x64-HudStats'),
    [string] $Output = (Join-Path $PSScriptRoot '../Builds/Installer')
)
$ErrorActionPreference = 'Stop'
$compiler = (Resolve-Path -LiteralPath $Iscc).Path
$source = (Resolve-Path -LiteralPath $Release).Path
$requiredFiles = 'RTS.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe'
$requiredDirs = 'RTS_Data', 'MonoBleedingEdge', 'D3D12'
foreach ($name in $requiredFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Missing release file: $name" }
}
foreach ($name in $requiredDirs) {
    if (!(Test-Path -LiteralPath (Join-Path $source $name) -PathType Container)) { throw "Missing release directory: $name" }
}

$assembly = Join-Path $source 'RTS_Data/Managed/Assembly-CSharp.dll'
$runtimeMetadata = Join-Path $source 'RTS_Data/RuntimeInitializeOnLoads.json'
$code = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($assembly))
if ($code -match 'ReleaseWindowProbe|Temporary(?:Acceptance|CavalryProbe|SaveProbe|MatchArtCheck|MenuArtProbe|FinalProbe|FogRuntime|StyleCapture|AudioProbe)' -or
    (Get-Content -LiteralPath $runtimeMetadata -Raw) -match 'ReleaseWindowProbe|Temporary') {
    throw 'The input contains verification modules. Use the clean release player.'
}
$destination = (New-Item -ItemType Directory -Path $Output -Force).FullName
$stage = Join-Path ([IO.Path]::GetTempPath()) ('rts-installer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    foreach ($name in $requiredFiles) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $stage
    }
    foreach ($name in $requiredDirs) {
        $folder = Join-Path $source $name
        foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File) {
            $relative = $file.FullName.Substring($source.Length + 1)
            if ($relative -match '(^|[\\/])(Saves|Logs|Tests|ReleaseQA|[^\\/]*DoNotShip)([\\/]|$)' -or
                $file.Extension -in '.log', '.pdb', '.mdb', '.bak', '.tmp' -or
                $file.Name -match '^(Test|Temporary|.*Probe)') { continue }
            $target = Join-Path $stage $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
    & $compiler "/DPayload=$stage" "/O$destination" (Join-Path $PSScriptRoot 'RTS.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup returned $LASTEXITCODE." }
    Get-FileHash -LiteralPath (Join-Path $destination 'RTS-0.1.0-Windows-x64-Setup.exe')
}
finally {
    $fullStage = [IO.Path]::GetFullPath($stage)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($fullStage) -eq $tempRoot -and [IO.Path]::GetFileName($fullStage) -match '^rts-installer-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $fullStage -Recurse -Force
    }
}
