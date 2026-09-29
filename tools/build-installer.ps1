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

# Tool locations differ per machine, so nothing absolute is hardcoded in this file — it is
# committed and shared, and a path that is right here is wrong everywhere else. A machine
# that keeps its SDK somewhere unusual says so once, in tools\local.build.json (git-ignored)
# or in the MINICLIP_DOTNET / MINICLIP_ISCC environment variables. See docs/BUILD.md.
$localConfigPath = Join-Path $PSScriptRoot 'local.build.json'
$localConfig = $null
if (Test-Path -LiteralPath $localConfigPath) {
    try {
        $localConfig = Get-Content -LiteralPath $localConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "local.build.json is not valid JSON: $($_.Exception.Message)"
    }
}

# A candidate counts only if it really reports a 10.x SDK. The `dotnet` first on PATH is
# often a runtime-only install, and it must not win over a real SDK found further down.
$isDotnet10Sdk = { param($exe) @(& $exe --list-sdks 2>$null) -match '^10\.' }

if (-not $DotnetExe) { $DotnetExe = $localConfig.dotnetExe }
if (-not $DotnetExe) { $DotnetExe = $env:MINICLIP_DOTNET }

if (-not $DotnetExe) {
    $programRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA) |
        Where-Object { $_ }
    $onPath = Get-Command dotnet.exe -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    # The statements are separated by newlines rather than commas on purpose: `@(a, (pipe))`
    # keeps the pipe's result as a single nested array, so a candidate would arrive at
    # `& $isDotnet10Sdk` as an array and be run as one space-joined command name.
    $candidates = @(
        $onPath
        $programRoots | ForEach-Object { Join-Path $_ 'dotnet\dotnet.exe' }
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $DotnetExe = $candidates | Where-Object { & $isDotnet10Sdk $_ } | Select-Object -First 1
}

if (-not $DotnetExe) {
    throw 'A .NET 10 SDK was not found. Pass -DotnetExe, set MINICLIP_DOTNET, or create tools\local.build.json (see docs\BUILD.md).'
}
if (-not (Test-Path -LiteralPath $DotnetExe)) {
    throw "The .NET SDK to use does not exist: $DotnetExe"
}
if (-not (& $isDotnet10Sdk $DotnetExe)) {
    throw "'$DotnetExe' does not report a .NET 10 SDK. Run '$DotnetExe --list-sdks' to see what it has."
}

if (-not $IsccExe) { $IsccExe = $localConfig.isccExe }
if (-not $IsccExe) { $IsccExe = $env:MINICLIP_ISCC }

if (-not $IsccExe) {
    # Same newline-separated form as the SDK list above, for the same reason.
    $isccOnPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    $isccCandidates = @(
        $isccOnPath
        'C:\Program Files\Inno Setup 7\ISCC.exe'
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    )
    if ($env:LOCALAPPDATA) {
        $isccCandidates += (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
    }
    $IsccExe = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $IsccExe -or -not (Test-Path -LiteralPath $IsccExe)) {
    throw 'Inno Setup ISCC.exe was not found. Pass -IsccExe, set MINICLIP_ISCC, or create tools\local.build.json (see docs\BUILD.md).'
}

# Read the version with an explicit UTF-8 read.
#
# This used to be `Get-Content -Raw | [xml]`, which broke as soon as the project gained a
# non-ASCII value: the .csproj has no byte-order mark, and Windows PowerShell 5.1's
# Get-Content decodes BOM-less files using the ANSI code page. The Chinese <Description>
# then decoded to mojibake whose "</Description>" tail was mangled, so XML parsing failed
# with a baffling "start tag does not match end tag" error. Reading through
# [System.IO.File]::ReadAllText uses UTF-8 and is immune to the console code page.
# The version is taken from the first <PropertyGroup> that actually declares one, not from
# `$projectXml.Project.PropertyGroup.Version`. That shorter form breaks as soon as the
# project has more than one <PropertyGroup> — which is ordinary, and is exactly what adding
# a configuration-conditioned group does. PropertyGroup then becomes an array, .Version
# yields an array holding the version and $null, and casting that to a string joins the two
# with a space: the version silently became "1.0.0 " and the installer was emitted as
# "MiniClip-Setup-1.0.0 -x64.exe". Take the first non-empty value instead.
$projectText = [System.IO.File]::ReadAllText($projectPath, [System.Text.Encoding]::UTF8)
[xml]$projectXml = $projectText
$version = @(
    $projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }
) | Select-Object -First 1
$version = "$version".Trim()
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
