# Builds Release and assembles a ready-to-load Dynamo package folder under dist\.
# Load it by adding dist\ (or a copy of the package folder's parent) to Dynamo's
# Preferences > Package Settings > Node and Package Paths.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$manifest = Get-Content (Join-Path $PSScriptRoot 'pkg.json') -Raw | ConvertFrom-Json
$target = Join-Path $root "dist\$($manifest.name)"

dotnet build (Join-Path $root 'src\CivilDynamoTools.Diagnostics\CivilDynamoTools.Diagnostics.csproj') -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $target 'bin'), (Join-Path $target 'dyf'), (Join-Path $target 'extra') | Out-Null

Copy-Item (Join-Path $PSScriptRoot 'pkg.json') $target
$bin = Join-Path $root 'src\CivilDynamoTools.Diagnostics\bin\Release'
# Only the node library is listed in pkg.json node_libraries; the geometry DLL ships as a dependency.
foreach ($dll in 'CivilDynamoTools.Diagnostics.dll', 'CivilDynamoTools.Diagnostics.Geometry.dll') {
    Copy-Item (Join-Path $bin $dll) (Join-Path $target 'bin')
}

Write-Host "Package assembled at: $target"
