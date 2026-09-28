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

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if (-not $version) {
    throw 'The MiniClip project has no Version property.'
}

New-Item -ItemType Directory -Path $stagingDir, $outputDir -Force | Out-Null

Write-Host "Publishing MiniClip $version (self-contained win-x64)..."
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

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Write-Host "Installer: $installerPath"
Write-Host "SHA256: $($hash.Hash)"
Write-Host "Published files: $stagingDir"
