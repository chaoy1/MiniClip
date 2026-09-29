param(
    [string]$DotnetExe,
    [string]$IsccExe
)

$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'src\MiniClip\MiniClip.csproj'
$scriptPath = Join-Path $projectRoot 'installer\MiniClip.iss'
$outputDir = Join-Path $projectRoot 'dist'
$stagingDir = Join-Path $projectRoot ("artifacts\installer\publish-{0}" -f [guid]::NewGuid().ToString('N'))

if (-not $DotnetExe) {
    $candidates = @(
        (Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        'D:\Software\dotnet-sdk\dotnet.exe'
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $DotnetExe = $candidates | Where-Object { @(& $_ --list-sdks) -match '^10\.' } | Select-Object -First 1
}
if (-not $DotnetExe -or -not (Test-Path -LiteralPath $DotnetExe)) {
    throw 'A .NET 10 SDK was not found. Pass -DotnetExe with the SDK dotnet.exe path.'
}

if (-not $IsccExe) {
    $candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $IsccExe = $candidates | Select-Object -First 1
}
if (-not $IsccExe -or -not (Test-Path -LiteralPath $IsccExe)) {
    throw 'Inno Setup ISCC.exe was not found. Pass -IsccExe with the compiler path.'
}

# Read the version with an explicit UTF-8 read.
#
# This used to be `Get-Content -Raw | [xml]`, which broke as soon as the project gained a
# non-ASCII value: the .csproj has no byte-order mark, and Windows PowerShell 5.1's
# Get-Content decodes BOM-less files using the ANSI code page. The Chinese <Description>
# then decoded to mojibake whose "</Description>" tail was mangled, so XML parsing failed
# with a baffling "start tag does not match end tag" error. Reading through
# [System.IO.File]::ReadAllText uses UTF-8 and is immune to the console code page.
$projectText = [System.IO.File]::ReadAllText($projectPath, [System.Text.Encoding]::UTF8)
[xml]$projectXml = $projectText
$version = [string]$projectXml.Project.PropertyGroup.Version
if (-not $version) {
    throw 'The MiniClip project has no Version property.'
}

New-Item -ItemType Directory -Path $stagingDir, $outputDir -Force | Out-Null

# The staging publish is a full self-contained runtime: ~156 MB across 269 files. It is an
# intermediate, not a deliverable, so it must not survive the build. An earlier version of
# this script left every one of them behind, and a dozen builds silently accumulated over
# 2 GB in artifacts\installer — with no clue from the output that anything was wrong.
try {
    Write-Host "Publishing MiniClip $version (self-contained win-x64, staging)..."
    & $DotnetExe publish $projectPath -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=false' -o $stagingDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $publishedExe = Join-Path $stagingDir 'MiniClip.exe'
    if (-not (Test-Path -LiteralPath $publishedExe)) {
        throw "Publish completed without MiniClip.exe: $stagingDir"
    }

    Write-Host 'Compiling MiniClip installer...'
    & $IsccExe "/DPublishDir=$stagingDir" "/DOutputDir=$outputDir" "/DAppVersion=$version" $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }

    $installerPath = Join-Path $outputDir "MiniClip-Setup-$version-x64.exe"
    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Inno Setup completed without the expected installer: $installerPath"
    }

    $installer = Get-Item -LiteralPath $installerPath
    Write-Host "Installer: $installerPath"
    Write-Host "Size: $($installer.Length) bytes"
    Write-Host "SHA256: $((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash)"
}
catch {
    # On failure the staging directory is the only copy of the publish output and the thing
    # you would inspect, so it is deliberately kept. Say where it is.
    Write-Warning "Build failed; staging directory kept for inspection: $stagingDir"
    throw
}
finally {
    if (Test-Path -LiteralPath $stagingDir) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $stagingDir) {
            Write-Warning "Could not remove the staging directory (a file may still be in use): $stagingDir"
        }
        else {
            Write-Host 'Staging directory removed.'
        }
    }
}
