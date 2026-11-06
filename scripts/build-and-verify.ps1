$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location (Join-Path $root 'AgriStreamIoT')
Write-Host 'Restoring solution packages...'
dotnet restore .\AgriStreamIoT.sln
Write-Host 'Building MVC + Functions projects...'
dotnet build .\AgriStreamIoT.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed. Copy the complete build output for troubleshooting.' }
Write-Host 'Build succeeded. Azure resource connectivity still needs to be tested with your configured connection strings.'
