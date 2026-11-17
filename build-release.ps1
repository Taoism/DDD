<#
.SYNOPSIS
  Builds the DDD release packages into .\release:
    DDD-<v>-setup-win-x64.exe      Windows installer (Inno Setup 6)
    DDD-<v>-portable-win-x64.zip   portable: unzip anywhere; settings stay in its Data folder
    SHA256SUMS.txt
  Both have the app (DDD.exe) and the command-line tool (ddd-cli.exe), self-contained (no .NET install needed).
#>
param(
    [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "DDD $Version" -ForegroundColor Cyan

$release = Join-Path $PSScriptRoot 'release'
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory $release | Out-Null

$out = Join-Path $PSScriptRoot 'publish\win-x64'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
# each program is published to its own folder first: a second single-file publish into the same
# folder removes the first one's output
foreach ($project in 'src/DDD/DDD.csproj', 'src/DDD.Cli/DDD.Cli.csproj') {
    Write-Host "publish $project"
    $one = Join-Path $PSScriptRoot ('publish\' + [IO.Path]::GetFileNameWithoutExtension($project))
    if (Test-Path $one) { Remove-Item $one -Recurse -Force }
    dotnet publish $project -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $one -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "publish $project failed" }
    Copy-Item (Join-Path $one '*') $out -Recurse -Force
}
foreach ($exe in 'DDD.exe', 'ddd-cli.exe') {
    if (-not (Test-Path (Join-Path $out $exe))) { throw "$exe is missing from $out" }
}

# installer
& $Iscc /Q "/DAppVersion=$Version" "/DSourceDir=$out" "/O$release" installer\ddd-setup.iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

# portable zip: portable.txt switches settings and cache to the Data folder next to DDD.exe
$stage = Join-Path $env:TEMP "ddd-zip-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$zipRoot = Join-Path $stage "DDD-$Version"
Copy-Item $out $zipRoot -Recurse
Copy-Item LICENSE, README.md, THIRD-PARTY-NOTICES.txt $zipRoot
Set-Content (Join-Path $zipRoot 'portable.txt') -Encoding ascii -Value @(
    'This file makes DDD portable: settings and cached catalog searches are kept in the Data folder here.',
    'Delete it to use %APPDATA%\DDD and %LOCALAPPDATA%\DDD instead.')
Compress-Archive -Path $zipRoot -DestinationPath (Join-Path $release "DDD-$Version-portable-win-x64.zip") -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

# checksums
Push-Location $release
Get-ChildItem -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content SHA256SUMS.txt -Encoding ascii
Pop-Location

Get-ChildItem $release | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
