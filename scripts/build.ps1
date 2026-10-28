param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Command failed: $LASTEXITCODE" } }
node scripts/build-ui.mjs
Check-Exit
foreach ($file in @('app.js', 'lens.mjs', 'lens-scene.mjs', 'lens-view.mjs')) {
  node --check "src/Visions.Viewer/Assets/$file"
  Check-Exit
}
node tests/process-lens.test.mjs
Check-Exit
node tests/process-lens-ui.test.mjs
Check-Exit
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
Get-ChildItem $package -Filter '*.pdb' -File | Remove-Item
Copy-Item docs/USAGE.md (Join-Path $package 'READ-ME.md')
Copy-Item docs/DEPENDENCIES.md $package
$assets = Get-Content src/Visions.Windows/obj/project.assets.json -Raw | ConvertFrom-Json
$noticeRoot = Join-Path $package 'licenses'
New-Item $noticeRoot -ItemType Directory -Force | Out-Null
$uiNotices = Join-Path $noticeRoot 'ui'
New-Item $uiNotices -ItemType Directory -Force | Out-Null
Get-ChildItem src/Visions.Viewer/Assets/ui -File | Where-Object { $_.Name -ne 'package.json' } | Copy-Item -Destination $uiNotices
function Copy-Notices([string]$Location, [string]$Name) {
  $destination = Join-Path $noticeRoot $Name
  New-Item $destination -ItemType Directory -Force | Out-Null
  Get-ChildItem $Location -File | Where-Object { $_.Name -match '(?i)license|notice|\.nuspec$' } | Copy-Item -Destination $destination
}
foreach ($lib in $assets.libraries.PSObject.Properties) {
  if ($lib.Value.type -ne 'package') { continue }
  foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $location = Join-Path $folder $lib.Name.ToLowerInvariant()
    if (Test-Path $location) { Copy-Notices $location ($lib.Name -replace '/', '-') }
  }
}
# Self-contained framework packs are not listed in assets.libraries. Include their notices separately.
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.aspnetcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
  $found = $false
  foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $location = Join-Path $folder $runtime
    if (-not (Test-Path $location)) { continue }
    foreach ($version in Get-ChildItem $location -Directory) {
      $license = @(Get-ChildItem $version.FullName -File | Where-Object { $_.Name -match '(?i)^license' })
      if ($license.Count -eq 0) { continue }
      Copy-Notices $version.FullName "$runtime-$($version.Name)"
      $found = $true
    }
  }
  if (-not $found) { throw "Runtime license missing: $runtime" }
}
dotnet list src/Visions.Windows package --include-transitive --format json | Set-Content (Join-Path $package 'dependencies.json') -Encoding utf8
Check-Exit
$sourceCommit = git rev-parse HEAD
Check-Exit
$sourceTree = git rev-parse 'HEAD^{tree}'
Check-Exit
$sdk = dotnet --version
Check-Exit
@{ source_commit = $sourceCommit; source_tree = $sourceTree; sdk = $sdk; target = 'win-x64'; preview = $true } |
  ConvertTo-Json | Set-Content (Join-Path $package 'build-info.json') -Encoding utf8
$smoke = Join-Path ([System.IO.Path]::GetTempPath()) ('visions-smoke-' + [guid]::NewGuid().ToString('N'))
try {
  foreach ($kind in @('smoke', 'uia-probe')) {
    $process = Start-Process (Join-Path $package 'Visions.exe') -ArgumentList @("--$kind", ('"' + $smoke + '"')) -PassThru
    if (-not $process.WaitForExit(40000)) { $process.Kill(); throw "Published executable $kind timeout." }
    if ($process.ExitCode -ne 0 -or -not (Test-Path (Join-Path $smoke "$kind-ok.txt"))) { throw "Published executable $kind failed." }
    Copy-Item (Join-Path $smoke "$kind-ok.txt") $package
    Get-Content (Join-Path $smoke "$kind-ok.txt")
  }
} finally { if (Test-Path $smoke) { Remove-Item $smoke -Recurse -Force } }
$stamp = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow, 'Tokyo Standard Time').ToString('yyMMddHHmmss')
$zip = Join-Path (Get-Location) "dist/$stamp.id-pending.dist.zip"
Compress-Archive -Path "$package/*" -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding ascii
Write-Host "PACKAGE: $zip"
