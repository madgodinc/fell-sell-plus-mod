# Builds the player release: dist/FellSellPlus-<version>-Setup.exe, one file with MelonLoader, the mod
# and the configurator inside. Game art is not included (the mod exports it on the player's PC).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$ver = ([xml](Get-Content "$root\src\FellSellPlus\FellSellPlus.csproj")).Project.PropertyGroup[0].Version

Write-Host "1/4 mod"
dotnet build "$root\src\FellSellPlus\FellSellPlus.csproj" -c Release -v q -nologo | Out-Null
if ($LASTEXITCODE) { throw "mod build failed" }

Write-Host "2/4 configurator"
$cfgOut = "$root\tools\Configurator\publish"
dotnet publish "$root\tools\Configurator\Configurator.csproj" -c Release -o $cfgOut -v q -nologo | Out-Null
if ($LASTEXITCODE) { throw "configurator build failed" }

Write-Host "3/4 payload"
$stage = Join-Path $env:TEMP "fsp-release-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\Mods", "$stage\UserData\FellSellPlus\Configurator" | Out-Null
Copy-Item "$root\vendor\melonloader\*" $stage -Recurse -Force
Copy-Item "$root\src\FellSellPlus\bin\Release\net6.0\FellSellPlus.dll" "$stage\Mods\" -Force
Copy-Item "$cfgOut\*" "$stage\UserData\FellSellPlus\Configurator\" -Recurse -Force -Exclude *.pdb
# MelonLoader's runtimeconfig with tiered compilation already off (it crashes this Unity build;
# the mod fixes it too, but only from the second launch).
$rc = "$stage\MelonLoader\net6\MelonLoader.runtimeconfig.json"
if ((Test-Path $rc) -and -not (Select-String -Path $rc -Pattern "TieredCompilation" -Quiet)) {
    $t = (Get-Content $rc -Raw) -replace '"configProperties": \{', "`"configProperties`": {`n      `"System.Runtime.TieredCompilation`": false,"
    [IO.File]::WriteAllText($rc, $t)
}
$zip = "$root\tools\Installer\payload.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "4/4 setup"
dotnet build "$root\tools\Installer\Installer.csproj" -c Release -v q -nologo | Out-Null
if ($LASTEXITCODE) { throw "setup build failed" }
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
$out = "$root\dist\FellSellPlus-$ver-Setup.exe"
Copy-Item "$root\tools\Installer\bin\Release\net48\FellSellPlus-Setup.exe" $out -Force
# Nexus takes archives only: the setup also goes out as a zip holding just the exe.
$setupZip = "$root\dist\FellSellPlus-$ver-Setup.zip"
if (Test-Path $setupZip) { Remove-Item $setupZip -Force }
$za = [System.IO.Compression.ZipFile]::Open($setupZip, "Create")
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $out, "FellSellPlus-$ver-Setup.exe", "Optimal") | Out-Null
$za.Dispose()
"{0}  {1:N1} MB" -f $out, ((Get-Item $out).Length / 1MB)

# Manual archive for players who prefer not to run an exe: the same files plus install notes.
Copy-Item "$root\docs\INSTALL-MANUAL.txt" "$stage\Fell & Sell Plus - install (RU-EN).txt" -Force
$manual = "$root\dist\FellSellPlus-$ver-Manual.zip"
if (Test-Path $manual) { Remove-Item $manual -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $manual, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force
"{0}  {1:N1} MB" -f $manual, ((Get-Item $manual).Length / 1MB)
