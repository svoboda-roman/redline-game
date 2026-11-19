# Windows twin of make_char.sh: model + animate (Blender) -> render (Blender) -> bake + pack (Blender's Python).
# Usage: powershell -File tools/art/common/make_char.ps1 -Script tools/art/enemies/grunt.py -Name grunt [-Out dir] [-Preview dir]
# Env/params: -Blender path to blender.exe (default: ../blender next to the repo), -Ppu 48, -Scale 8, -Only "idle,walk"
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Out,
    [string]$Preview,
    [string]$Blender,
    [int]$Ppu = 48,
    [int]$Scale = 8,
    [string]$Only
)
$ErrorActionPreference = 'Continue'   # Blender and Python log to stderr; failures are checked by their outputs
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path "$here\..\..\..").Path
if (-not $Blender) { $Blender = (Get-ChildItem "$repo\..\blender\*\blender.exe" | Select-Object -First 1).FullName }
$py = (Get-ChildItem (Join-Path (Split-Path $Blender) '*\python\bin\python.exe') | Select-Object -First 1).FullName
if (-not $Out) { $Out = "$repo\Assets\Resources\Art\Characters\" + ($Name.Substring(0, 1).ToUpper() + $Name.Substring(1)) }
$work = Join-Path $env:TEMP "redline_chars\$Name"
$render = Join-Path $work ("render_ppu{0}_x{1}_{2}" -f $Ppu, $Scale, [DateTimeOffset]::Now.ToUnixTimeSeconds())
New-Item -ItemType Directory -Force $work, $render, $Out | Out-Null
$blend = Join-Path $work "$Name.blend"
$scriptPath = (Resolve-Path $Script).Path

Write-Host "== ${Name}: model + animate"
& $Blender -b --factory-startup -P $scriptPath -- --out $blend 2>&1 | Where-Object { "$_" -match 'SAVED|Error|Traceback|line [0-9]' } | ForEach-Object { Write-Host $_ }
if (-not (Test-Path $blend)) { throw "build failed" }
Write-Host "== ${Name}: render (PPU $Ppu, x$Scale)"
$ra = @('-b', $blend, '-P', "$here\render_char.py", '--', '--ppu', $Ppu, '--scale', $Scale, '--out', $render)
if ($Only) { $ra += @('--only', $Only) }
& $Blender @ra 2>&1 | Where-Object { "$_" -match 'Error|Traceback|line [0-9]' } | ForEach-Object { Write-Host $_ }
if (-not (Test-Path "$render\manifest.json")) { throw "render failed" }
Write-Host "== ${Name}: pack (bake_char.py)"
$ba = @("$here\bake_char.py", '--render', $render, '--out', $Out, '--name', $Name)
if ($Preview) { New-Item -ItemType Directory -Force $Preview | Out-Null; $ba += @('--preview', $Preview) }
& $py @ba
if ($LASTEXITCODE -ne 0) { throw "bake failed" }
Write-Host "== ${Name}: done -> $Out"
