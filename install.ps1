# PV Local - per-user install. No admin needed.
#   powershell -ExecutionPolicy Bypass -File install.ps1            (install)
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
# Finds every AutoCAD 2021-2026 / CADPILLAR 11 profile for this user, installs the matching build,
# registers it to load at startup and adds its folder to that product's TRUSTEDPATHS (existing paths kept).
#   AutoCAD R24.x (2021-2024)           -> bin\net48  (.NET Framework 4.8)
#   AutoCAD R25.x (2025-2026), CADPILLAR -> bin\net8   (.NET 8)
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:APPDATA 'PVLocal'

$products = @()
foreach ($r in Get-ChildItem 'HKCU:\SOFTWARE\Autodesk\AutoCAD\R24.*', 'HKCU:\SOFTWARE\Autodesk\AutoCAD\R25.*' -ErrorAction SilentlyContinue) {
    $build = if ($r.PSChildName -like 'R24.*') { 'net48' } else { 'net8' }
    Get-ChildItem $r.PSPath | ForEach-Object { $products += [pscustomobject]@{ Key = $_; Build = $build } }
}
foreach ($r in Get-ChildItem 'HKCU:\SOFTWARE\Digicorp Ingegneria\CADPILLAR\*' -ErrorAction SilentlyContinue) {
    Get-ChildItem $r.PSPath | ForEach-Object { $products += [pscustomobject]@{ Key = $_; Build = 'net8' } }
}
$products = $products | Where-Object { Test-Path (Join-Path $_.Key.PSPath 'Applications') }
if (-not $products) { throw 'No AutoCAD 2021-2026 / CADPILLAR 11 user profile found. Start the program once, then rerun.' }

function Set-Trusted($productKey, $folder, [switch]$Remove) {
    Get-ChildItem (Join-Path $productKey.PSPath 'Profiles') -ErrorAction SilentlyContinue | ForEach-Object {
        $v = Join-Path $_.PSPath 'Variables'
        if (-not (Test-Path $v)) { if ($Remove) { return }; New-Item $v | Out-Null }
        $paths = @(((Get-ItemProperty $v -ErrorAction SilentlyContinue).TRUSTEDPATHS -split ';') | Where-Object { $_ -and $_ -notlike "$root*" })
        if (-not $Remove) { $paths += $folder }
        Set-ItemProperty $v TRUSTEDPATHS ($paths -join ';')
    }
}

foreach ($p in $products) {
    $key = Join-Path $p.Key.PSPath 'Applications\PVLocal'
    $dir = Join-Path $root $p.Build
    if ($Uninstall) {
        Remove-Item $key -Recurse -ErrorAction SilentlyContinue
        Set-Trusted $p.Key $dir -Remove
        "removed: $($p.Key.Name)"; continue
    }
    New-Item $dir -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "bin\$($p.Build)\PVLocal.dll") $dir -Force
    New-Item $key -Force | Out-Null
    Set-ItemProperty $key DESCRIPTION 'PV Local - solar plant layout'
    Set-ItemProperty $key LOADER (Join-Path $dir 'PVLocal.dll')
    Set-ItemProperty $key LOADCTRLS 2 -Type DWord   # 2 = load at startup
    Set-ItemProperty $key MANAGED 1 -Type DWord
    Set-Trusted $p.Key $dir
    "installed $($p.Build): $($p.Key.Name)"
}
if ($Uninstall) { Remove-Item $root -Recurse -ErrorAction SilentlyContinue; return }
'Restart AutoCAD/CADPILLAR, then type PVSETTINGS / PVLAYOUT / PVCOMPARE / PVCLEAR'
