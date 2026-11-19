# Builds Assets/Resources/City01.txt by hand from the player's drawing (tools/level/City01-drawing-v4.png).
# Every element is a rectangle in drawing pixels; 17 px = 1 tile. Interior: x 120..1450, y 135..680.
# Usage: powershell -File tools/level/City01-build.ps1 [-Out path] [-Remix]
param([string]$Out = "$PSScriptRoot\..\..\Assets\Resources\City01.txt", [switch]$Remix, [switch]$Top)

$S = 17.0; $X0 = 120; $Y0 = 135
$W = 82; $H = 35                       # 2 wall cols each side, 2 ceiling rows, 32 interior rows, 1 floor row
$g = New-Object 'char[,]' $H, $W
for ($r = 0; $r -lt $H; $r++) { for ($c = 0; $c -lt $W; $c++) {
    $g[$r, $c] = if ($r -lt 2 -or $r -eq $H - 1 -or $c -lt 2 -or $c -ge $W - 2) { '#' } else { '.' } } }

function Col([double]$x) { [int][math]::Floor(($x - $X0) / $S) + 2 }
function Row([double]$y) { [int][math]::Floor(($y - $Y0) / $S) + 2 }
function Clip($v, $lo, $hi) { [math]::Max($lo, [math]::Min($hi, $v)) }

# Blue: thin (< 24 px) sides collapse to one tile at their centre line.
function Solid($x1, $x2, $y1, $y2) {
    if ($x2 - $x1 -lt 24) { $c1 = $c2 = Col (($x1 + $x2) / 2) } else { $c1 = Col ($x1 + 4); $c2 = Col ($x2 - 4) }
    if ($y2 - $y1 -lt 24) { $r1 = $r2 = Row (($y1 + $y2) / 2) } else { $r1 = Row ($y1 + 4); $r2 = Row ($y2 - 4) }
    for ($r = $r1; $r -le $r2; $r++) { for ($c = $c1; $c -le $c2; $c++) { $g[(Clip $r 0 ($H-1)), (Clip $c 0 ($W-1))] = '#' } }
}
# Red: a strip of spikes on the free side of what it touches. side: up (on top), down (under), left, right.
function Spike($x1, $x2, $y1, $y2, $side) {
    if ($side -eq 'up' -or $side -eq 'down') {
        $c1 = Col ($x1 + 3); $c2 = Col ($x2 - 3)
        for ($c = $c1; $c -le $c2; $c++) {
            $r = Row (($y1 + $y2) / 2)
            $step = if ($side -eq 'up') { -1 } else { 1 }
            while ($g[$r, $c] -eq '#') { $r += $step }
            $g[$r, $c] = '^'
        }
    } else {
        $r1 = Row ($y1 + 3); $r2 = Row ($y2 - 3)
        for ($r = $r1; $r -le $r2; $r++) {
            $c = Col (($x1 + $x2) / 2)
            $step = if ($side -eq 'left') { -1 } else { 1 }
            while ($g[$r, $c] -eq '#') { $c += $step }
            $g[$r, $c] = '^'
        }
    }
}

# ---- upper left: long ledge with a spiked wall at its right end
Solid 212 260 158 170;  Solid 363 402 165 180;  Solid 476 511 213 228
Solid 620 735 212 227;  Solid 733 745 208 297;  Spike 745 760 207 298 right
Solid 148 745 281 298;  Spike 200 272 267 283 up;  Spike 683 707 267 283 up
Solid 244 364 310 323
Solid 152 212 352 363;  Spike 152 212 341 353 up
Solid 125 186 415 426
Solid 120 133 437 602                                  # thickened left wall
Solid 170 279 494 508;  Spike 170 230 482 494 up
Solid 142 196 579 593
# ---- middle: post-capped ledge, overhang and the spiked shaft
Solid 323 505 550 563;  Spike 368 412 543 551 up;  Solid 497 508 528 591
Solid 408 462 416 427
Solid 463 569 374 402;  Spike 463 484 400 414 down
if ($Remix) {
    # idea: the spiked shaft one tile wider (2 free tiles instead of 1), so it is a skill drop, not a coin flip
    Solid 569 667 380 396;  Solid 667 683 383 625
    Solid 577 595 451 627;  Spike 596 612 455 605 right; Spike 651 663 453 605 left
} else {
    Solid 569 650 380 396;  Solid 650 666 383 625
    Solid 577 595 451 627;  Spike 596 612 455 605 right; Spike 634 646 453 605 left
}
# ---- right: floating blocks, staircase and the long ledge
Solid 830 895 360 376;  Solid 892 958 204 222;  Solid 985 1047 284 298
Solid 1111 1238 322 342; Spike 1113 1145 341 351 down
Solid 1159 1376 185 201; Spike 1252 1299 169 184 up
Solid 1362 1447 251 268; Solid 1409 1447 375 393; Solid 1276 1338 408 434; Solid 1162 1200 447 463
Solid 766 825 588 608;  Spike 779 808 580 590 up
Solid 822 866 580 601;  Solid 853 900 564 583;  Solid 895 940 552 585
Solid 928 980 541 558;  Solid 978 999 527 553
Solid 994 1450 513 530; Spike 1184 1208 499 513 up; Spike 1384 1415 503 513 up
# ---- floor spikes
Spike 435 466 667 680 up
Spike 868 1370 663 680 up

if ($Remix) {
    # Claude's ideas on top of the drawing:
    # 1) a safe island in the middle of the long floor spike pit (2 tiles high): a rest stop under the ledge
    for ($c = (Col 1090); $c -le (Col 1140); $c++) { $g[($H-2), $c] = '#'; $g[($H-3), $c] = '#' }
    # 2) a 3-tile hole in the long upper ledge: drop through it into the middle instead of walking to its end
    for ($c = (Col 430); $c -le (Col 470); $c++) { $g[(Row 289), $c] = '.' }
    # 3) a low step near the start, so the first ledges are reachable without a wall jump
    Solid 230 290 625 637
    # 4) a hook rail over the empty top-middle, linking the left and right halves through the air
    Solid 790 840 150 160
}

if ($Remix -and $Top) {
    # Top pass (grid coords: row, col1, col2). The player is 1.6 tiles tall and jumps ~2.7 tiles.
    function Wipe($r, $c1, $c2) { for ($c = $c1; $c -le $c2; $c++) { $g[$r, $c] = '.' } }
    function Fill($r, $c1, $c2, $ch = '#') { for ($c = $c1; $c -le $c2; $c++) { $g[$r, $c] = $ch } }
    # a) top-left perch sat right under the ceiling (no room to stand): one row lower and a bit wider
    Wipe 3 7 10;  Fill 4 6 10
    # b) the top-middle hook rail: two rows lower so you can land on it, a stepping stone across the gap
    Wipe 3 41 44; Fill 5 41 45
    # c) a new step between the middle platform and the long right ledge (the widest empty gap up there)
    Fill 8 56 59
    # d) the long right ledge one row lower: its spikes get headroom, so you can jump over them on it
    Wipe 4 68 71; Wipe 5 63 75; Fill 6 63 75; Fill 5 68 71 '^'
    # e) the small block left of the spiked wall: wider, a landing spot before the wall
    Fill 7 22 25

    # ---- the pit right of the spiked shaft (cols 35-45, rows 17-33): was an empty dead end
    # g) the first stair loses its spikes (the floor under the stairs stays clear too)
    Wipe 28 40 42
    # h) a ledge on the shaft's outer wall: a perch for wall jumps up out of the pit
    Fill 24 35 37
    # i) a floating plate above the pit: hook point / stepping stone toward the bar over the shaft
    Fill 20 39 42

    # j) no spikes on the left half of the long right ledge (the player asked)
    Wipe 23 64 65
}

$g[($H - 2), 4] = 'P'                                  # start bottom left (the S in the drawing)

$name = if ($Remix) { 'City01 remix' } else { 'City01' }
$lines = @(";$name - neon street map drawn by the player (blue = platforms / walls, red = spikes); bounded (no wrap)", ';endless', ';randomspawn')
for ($r = 0; $r -lt $H; $r++) {
    $row = New-Object 'char[]' $W
    for ($c = 0; $c -lt $W; $c++) { $row[$c] = $g.GetValue($r, $c) }
    $lines += -join $row
}
[IO.File]::WriteAllLines((Resolve-Path -LiteralPath (Split-Path $Out) ).Path + '\' + (Split-Path $Out -Leaf), $lines)
$lines | Select-Object -Skip 3
