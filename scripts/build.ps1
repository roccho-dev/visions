param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Command failed: $LASTEXITCODE" } }
dotnet run --project tests/Visions.Tests -c Release
Check-Exit
if (-not $IsWindows) { if ($Publish) { throw 'Publishing and executable smoke tests require Windows.' }; exit 0 }
dotnet build src/Visions.Windows -c Release
Check-Exit
if (-not $Publish) { exit 0 }
$package = Join-Path (Get-Location) 'dist/package'
if (Test-Path $package) { Remove-Item $package -Recurse -Force }
New-Item $package -ItemType Directory -Force | Out-Null
dotnet publish src/Visions.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $package
Check-Exit
Copy-Item docs/USAGE.md (Join-Path $package 'READ-ME.md')
Copy-Item docs/DEPENDENCIES.md $package
# Preserve dependency attribution from the exact restored packages, including runtime packs.
$assets = Get-Content src/Visions.Windows/obj/project.assets.json -Raw | ConvertFrom-Json
$noticeRoot = Join-Path $package 'licenses'
New-Item $noticeRoot -ItemType Directory -Force | Out-Null
foreach ($lib in $assets.libraries.PSObject.Properties) {
  if ($lib.Value.type -ne 'package') { continue }
  foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $location = Join-Path $folder $lib.Name.ToLowerInvariant()
    if (-not (Test-Path $location)) { continue }
    $destination = Join-Path $noticeRoot ($lib.Name -replace '/', '-')
    New-Item $destination -ItemType Directory -Force | Out-Null
    Get-ChildItem $location -File | Where-Object { $_.Name -match '(?i)license|notice|\.nuspec$' } | Copy-Item -Destination $destination
  }
}
dotnet list src/Visions.Windows package --include-transitive --format json | Set-Content (Join-Path $package 'dependencies.json') -Encoding utf8
Check-Exit
$smoke = Join-Path ([System.IO.Path]::GetTempPath()) ('visions-smoke-' + [guid]::NewGuid().ToString('N'))
try {
  $process = Start-Process (Join-Path $package 'Visions.exe') -ArgumentList @('--smoke', ('"' + $smoke + '"')) -PassThru
  if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Published executable smoke timeout.' }
  if ($process.ExitCode -ne 0 -or -not (Test-Path (Join-Path $smoke 'smoke-ok.txt'))) { throw 'Published executable smoke failed.' }
  Copy-Item (Join-Path $smoke 'smoke-ok.txt') $package
} finally { if (Test-Path $smoke) { Remove-Item $smoke -Recurse -Force } }
$stamp = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, 'Tokyo Standard Time').ToString('yyMMddHHmmss')
# Thread id has not been supplied. The placeholder is explicit rather than an invented id.
$zip = Join-Path (Get-Location) "dist/$stamp.id-pending.dist.zip"
Compress-Archive -Path "$package/*" -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding ascii
Write-Host "PACKAGE: $zip"
