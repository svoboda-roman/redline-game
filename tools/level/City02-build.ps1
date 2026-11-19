# Builds Assets/Resources/City02.txt (level 2 "RAIL YARD"): same frame and rules as City01B (82 x 35, endless,
# random spawns, start bottom left), a different layout: a central climbing tower with a spiked face and
# ledges, two open spike pits (one with an island), a zigzag climb on the left, hook perches up high and
# a long right deck reached by stairs. Grid coords: row 0 = top, col 0 = left.
param([string]$Out = "$PSScriptRoot\..\..\Assets\Resources\City02.txt")
$W = 82; $H = 35
$g = New-Object 'char[,]' $H, $W
for ($r = 0; $r -lt $H; $r++) { for ($c = 0; $c -lt $W; $c++) {
    $g[$r, $c] = if ($r -lt 2 -or $r -eq $H - 1 -or $c -lt 2 -or $c -ge $W - 2) { '#' } else { '.' } } }
function Fill($r, $c1, $c2, $ch = '#') { for ($c = $c1; $c -le $c2; $c++) { $g[$r, $c] = $ch } }
function Block($r1, $r2, $c1, $c2, $ch = '#') { for ($r = $r1; $r -le $r2; $r++) { Fill $r $c1 $c2 $ch } }

# ---- floor: two open spike pits, an island in the second
Fill 33 20 26 '^'
Fill 33 52 63 '^'; Block 32 33 57 58

# ---- left: zigzag climb up to a long ledge
Fill 29 7 12
Fill 26 14 19
Fill 23 6 11
Fill 19 3 13; Fill 19 17 33          # long ledge with a drop hole at 14-16
Fill 18 22 24 '^'                      # spikes on top of it
Fill 15 3 5                            # wall perch, left

# ---- upper left: hook perches and a ceiling-side ledge with spikes hanging under it
Fill 8 5 8
Fill 6 14 17
Fill 9 24 28
Fill 12 18 33; Fill 13 26 28 '^'

# ---- centre: the tower (spiked left face, ledges on the right for wall-jump climbs)
Block 12 27 40 40
for ($r = 17; $r -le 23; $r++) { $g[$r, 39] = '^' }
Fill 14 41 44; Fill 20 41 44; Fill 26 41 44
Fill 30 34 38                          # a step in front of the tower's foot
Fill 5 36 44                           # cap rail above the tower

# ---- right: stairs up to a long deck, its spikes, hook blocks above
Fill 30 44 47
Fill 28 48 51
Fill 26 52 55
Fill 24 56 79                          # long deck to the right wall
Fill 23 62 64 '^'; Fill 23 72 73 '^'
Fill 16 50 58; Fill 15 53 54 '^'       # mid ledge with spikes on top
Block 8 9 60 63
Fill 12 70 74; Fill 13 71 72 '^'       # hanging spikes under it
Fill 6 52 56
Fill 18 77 79; Fill 30 77 79           # right wall perches

$g[($H - 2), 4] = 'P'
$lines = @(';City02 - RAIL YARD: level 2, a reworked take on City01B (tower, open pits, zigzag climb, right deck)', ';endless', ';randomspawn')
for ($r = 0; $r -lt $H; $r++) { $row = New-Object 'char[]' $W; for ($c = 0; $c -lt $W; $c++) { $row[$c] = $g.GetValue($r, $c) }; $lines += -join $row }
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($Out), $lines)
$lines | Select-Object -Skip 3
