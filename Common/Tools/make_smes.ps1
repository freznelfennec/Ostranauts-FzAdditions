#Run from anywhere: powershell -File Common\Tools\make_smes.ps1 [-Preview review.png]
param([string]$Src = "$PSScriptRoot\..\Art\smes", [string]$Out = "$PSScriptRoot\..\..\DataMods\FzAdditions\images\smes",
    [string]$Looks = "$PSScriptRoot\..\..\DataMods\FzAdditions\data\cooverlays\fz_cooverlays_smes.json", [string]$Preview)
#Builds the SMES unit's state sprites from the artist's set, whose files are named Pulsebank. Levels are pips lit per
#column, 0 to 6, filled from the bottom. Charging states are 2x2 sheets of 4 frames, read from the top left. Their glow
#layers are one image for each frame instead, the light sprite that shows them draws the whole texture whatever part of
#it is asked for.
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
Remove-Item "$Out\FzSmes*.png" #Everything here is written again below, nothing left over from an older layout

#Written images are named for the SMES unit rather than the artist's set, with the mod's Fz prefix so they can't
#collide with another mod's images
function Out-Name($name) { return 'Fz' + ($name -replace '^Pulsebank', 'Smes') }

function Load($name) { $b = [System.Drawing.Bitmap]::FromFile("$Src\$name.png"); $c = Copy-Bmp $b; $b.Dispose(); return $c }
function Copy-Bmp($b) { return $b.Clone([System.Drawing.Rectangle]::new(0, 0, 32, 32), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb) }
$clear = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)

$pipXs = 8, 15, 22
function PipRow($slot) { return 23 - 3 * $slot } #Slot 0 is the bottom pip
$barXs = 26..29
function BarRow($bar) { return 19 - 2 * $bar } #Bar 0 is the bottom light

$parts = @{}
foreach ($n in 'Pulsebank', 'Pulsebanklit', 'PulsebankOff', 'PulsebankCharging_f3', 'PulsebankCharging_f3lit', 'PulsebankDump_f0', 'PulsebankDump_f0lit', 'PulsebankBleed_f0', 'PulsebankBleed_f0lit') { $parts[$n] = Load $n }

#Mode: 'full' copies the body's pip, 'empty' the off unit's socket, 'dim' half way between with half the glow,
#'unlit' the body's pip without its glow
function Set-Pip($base, $lit, $body, $bodyLit, $slot, $mode) {
    $y0 = PipRow $slot
    foreach ($x0 in $pipXs) { foreach ($dx in 0, 1) { foreach ($dy in 0, 1) {
        $x = $x0 + $dx; $y = $y0 + $dy
        $full = $body.GetPixel($x, $y); $empty = $parts['PulsebankOff'].GetPixel($x, $y); $glow = $bodyLit.GetPixel($x, $y)
        switch ($mode) {
            'full' { $base.SetPixel($x, $y, $full); $lit.SetPixel($x, $y, $glow) }
            'empty' { $base.SetPixel($x, $y, $empty); $lit.SetPixel($x, $y, $clear) }
            'unlit' { $base.SetPixel($x, $y, $full); $lit.SetPixel($x, $y, $clear) }
            'dim' {
                $base.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, [int](($full.R + $empty.R) / 2), [int](($full.G + $empty.G) / 2), [int](($full.B + $empty.B) / 2)))
                $lit.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int]($glow.A / 2), $glow.R, $glow.G, $glow.B))
            }
        }
    } } }
}

function Set-Bar($base, $lit, $bar, $on) {
    $y = BarRow $bar
    foreach ($x in $barXs) {
        if ($on) { $base.SetPixel($x, $y, $parts['PulsebankBleed_f0'].GetPixel($x, $y)); $lit.SetPixel($x, $y, $parts['PulsebankBleed_f0lit'].GetPixel($x, $y)) }
        else { $base.SetPixel($x, $y, $parts['Pulsebank'].GetPixel($x, $y)); $lit.SetPixel($x, $y, $clear) }
    }
}

#Extra is the slot shown in extra mode, -1 for none. Bars is how many fast charge lights are on.
function New-Frame($body, $bodyLit, $level, $extra, $bars, $extraMode = 'dim') {
    $base = Copy-Bmp $parts[$body]; $lit = Copy-Bmp $parts[$bodyLit]
    for ($slot = 0; $slot -lt 6; $slot++) {
        $mode = if ($slot -lt $level) { 'full' } elseif ($slot -eq $extra) { $extraMode } else { 'empty' }
        Set-Pip $base $lit $parts[$body] $parts[$bodyLit] $slot $mode
    }
    for ($bar = 0; $bar -lt 4; $bar++) { Set-Bar $base $lit $bar ($bar -lt $bars) }
    return , @($base, $lit) #Kept whole through a pipeline
}

#The pack's art has its conduit arrow at the top right pointing right. The unit connects above its top left tile,
#the same as the game's battery, so every frame is turned a quarter anticlockwise as it is written. A normal map's
#directions turn with it: red is x to the right and green is y downwards, so (x, y) becomes (y, -x).
function Turn($bmp, $normal) {
    $turned = Copy-Bmp $bmp
    $turned.RotateFlip([System.Drawing.RotateFlipType]::Rotate270FlipNone)
    if ($normal) {
        for ($y = 0; $y -lt 32; $y++) { for ($x = 0; $x -lt 32; $x++) {
            $c = $turned.GetPixel($x, $y)
            $turned.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($c.A, $c.G, 255 - $c.R, $c.B))
        } }
    }
    return $turned
}

#Written under the name Out-Name gives it
function Save($bmp, $name, $normal = $false) { $turned = Turn $bmp $normal; $turned.Save("$Out\$(Out-Name $name).png", [System.Drawing.Imaging.ImageFormat]::Png); $turned.Dispose() }

#Each frame is turned on its own, so the frames keep their places in the sheet
function Save-Sheet($frames, $name, $normal = $false) {
    $sheet = New-Object System.Drawing.Bitmap 64, 64, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($sheet); $g.CompositingMode = 'SourceCopy'
    for ($k = 0; $k -lt $frames.Count; $k++) { $turned = Turn $frames[$k] $normal; $g.DrawImage($turned, ($k % 2) * 32, [math]::Floor($k / 2) * 32, 32, 32); $turned.Dispose() }
    $g.Dispose(); $sheet.Save("$Out\$(Out-Name $name).png", [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
}

#Idle and discharging count in half pips, 0 to 12: a pip holding part of its charge shows unlit, a full one lights up.
#Discharging flashes its part charged pip by switching between its look and the one below, FzSmesLook does that.
for ($half = 0; $half -le 12; $half++) {
    $full = [math]::Floor($half / 2); $part = if ($half % 2) { $full } else { -1 }
    $f = New-Frame 'Pulsebank' 'Pulsebanklit' $full $part 0 'unlit'
    Save $f[0] "PulsebankIdle_h$half"; Save $f[1] "PulsebankIdle_h$($half)lit"
    $f = New-Frame 'PulsebankDump_f0' 'PulsebankDump_f0lit' $full $part 0 'unlit'
    Save $f[0] "PulsebankDischarge_h$half"; Save $f[1] "PulsebankDischarge_h$($half)lit"
}

#Charging blinks the next pip, on for two frames and off for two. Fast charging also fills its four lights from the bottom.
for ($level = 0; $level -le 5; $level++) {
    $grid = 0..3 | % { New-Frame 'PulsebankCharging_f3' 'PulsebankCharging_f3lit' $level $(if ($_ -lt 2) { $level } else { -1 }) 0 }
    Save-Sheet ($grid | % { $_[0] }) "PulsebankGrid_p$level"; for ($k = 0; $k -lt 4; $k++) { Save $grid[$k][1] "PulsebankGrid_p$($level)lit_f$k" }
    $fast = 0..3 | % { New-Frame 'PulsebankCharging_f3' 'PulsebankCharging_f3lit' $level $(if ($_ -lt 2) { $level } else { -1 }) ($_ + 1) }
    Save-Sheet ($fast | % { $_[0] }) "PulsebankFast_p$level"; for ($k = 0; $k -lt 4; $k++) { Save $fast[$k][1] "PulsebankFast_p$($level)lit_f$k" }
}

#Normal and damage maps the sheets need laid out the same way
$n = Load 'Pulsebankn'; Save-Sheet @($n, $n, $n, $n) 'PulsebankSheetn' $true
$d = Load 'PulsebankDmg'; Save-Sheet @($d, $d, $d, $d) 'PulsebankDmgSheet'
foreach ($name in 'PulsebankOff', 'PulsebankDmg', 'PulsebankLoose', 'PulsebankDmgLoose') { $b = Load $name; Save $b $name; $b.Dispose() }
foreach ($name in 'Pulsebankn', 'PulsebankOffn', 'PulsebankDmgn', 'PulsebankLoosen', 'PulsebankDmgLoosen') { $b = Load $name; Save $b $name $true; $b.Dispose() }
$b = Load 'PulsebankDump_f0n'; Save $b 'PulsebankDischargen' $true; $b.Dispose()

#An off unit has nothing lit, every look has a glow layer so the unit can always swap to one
$blank = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb); Save $blank 'PulsebankOfflit'; $blank.Dispose()

#The COOverlays FzSmesLook picks the unit's look from, written here so they always match the images
$lookEntries = New-Object System.Collections.Generic.List[string]
function Add-Look($name, $img, $norm, $dmg, $rate) {
    $anim = if ($rate) { ",`r`n        `"objAnimation`" : { `"nFrameCount`" : 4, `"strFrameRate`" : `"$rate`", `"bLoop`" : true, `"bRandomStartingFrame`" : false, `"nSheetColumns`" : 2, `"nSheetRows`" : 2 }" } else { '' }
    $lookEntries.Add((@(
        '    {',
        "        `"strName`" : `"$name`",",
        '        "strNameFriendly" : "Basic SMES Unit",',
        '        "strNameShort" : "SMES Unit",',
        "        `"strImg`" : `"smes/$(Out-Name $img)`",",
        "        `"strImgNorm`" : `"smes/$(Out-Name $norm)`",",
        "        `"strImgDamaged`" : `"smes/$(Out-Name $dmg)`",",
        '        "strDmgColor" : "DamageTintNone",',
        '        "strPortraitImg" : "smes/FzSmesIdle_h12",',
        "        `"strCOBase`" : `"ItmFzSmes01`"$anim",
        '    }') -join "`r`n"))
}
Add-Look 'ItmFzSmes01Off' 'PulsebankOff' 'PulsebankOffn' 'PulsebankDmg' $null
0..12 | % { Add-Look "ItmFzSmes01Idle$_" "PulsebankIdle_h$_" 'Pulsebankn' 'PulsebankDmg' $null }
0..5 | % { Add-Look "ItmFzSmes01Grid$_" "PulsebankGrid_p$_" 'PulsebankSheetn' 'PulsebankDmgSheet' '4' }
0..5 | % { Add-Look "ItmFzSmes01Fast$_" "PulsebankFast_p$_" 'PulsebankSheetn' 'PulsebankDmgSheet' '8' }
0..12 | % { Add-Look "ItmFzSmes01Discharge$_" "PulsebankDischarge_h$_" 'PulsebankDischargen' 'PulsebankDmg' $null }
[IO.File]::WriteAllText($Looks, "[`r`n" + ($lookEntries -join ",`r`n") + "`r`n]`r`n", (New-Object Text.UTF8Encoding $false))

#Review sheet: each state's base over its glow on a dark ground, 6x magnified
if ($Preview) {
    $rows = @(
        @{ Label = 'Idle h0-h12'; Names = 0..12 | % { "PulsebankIdle_h$_" } },
        @{ Label = 'Discharge h0-h12'; Names = 0..12 | % { "PulsebankDischarge_h$_" } },
        @{ Label = 'Grid p0-p5 (frames 0, 2)'; Names = 0..5 | % { "PulsebankGrid_p$_" }; Sheet = $true; Frames = 0, 2 },
        @{ Label = 'Fast p2 (frames 0-3)'; Names = @('PulsebankFast_p2'); Sheet = $true; Frames = 0..3 },
        @{ Label = 'Fast p0-p5 (frame 3)'; Names = 0..5 | % { "PulsebankFast_p$_" }; Sheet = $true; Frames = @(3) },
        @{ Label = 'Off, Dmg, Loose, DmgLoose'; Names = 'PulsebankOff', 'PulsebankDmg', 'PulsebankLoose', 'PulsebankDmgLoose' }
    )
    $s = 6; $cell = 32 * $s + 8
    $bmp = New-Object System.Drawing.Bitmap (13 * $cell), ($rows.Count * ($cell + 22))
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(40, 40, 40)); $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
    $font = New-Object System.Drawing.Font 'Arial', 12
    for ($r = 0; $r -lt $rows.Count; $r++) {
        $row = $rows[$r]; $y = $r * ($cell + 22); $g.DrawString($row.Label, $font, [System.Drawing.Brushes]::White, 4, $y); $col = 0
        foreach ($name in $row.Names) {
            $img = [System.Drawing.Bitmap]::FromFile("$Out\$(Out-Name $name).png")
            $frames = if ($row.Sheet) { $row.Frames } else { @(0) }
            foreach ($k in $frames) {
                $rect = if ($row.Sheet) { [System.Drawing.Rectangle]::new(($k % 2) * 32, [math]::Floor($k / 2) * 32, 32, 32) } else { [System.Drawing.Rectangle]::new(0, 0, 32, 32) }
                $dest = [System.Drawing.Rectangle]::new($col * $cell, $y + 20, 32 * $s, 32 * $s)
                $litPath = if ($row.Sheet -and (Test-Path "$Out\$(Out-Name $name)lit_f$k.png")) { "$Out\$(Out-Name $name)lit_f$k.png" } else { "$Out\$(Out-Name $name)lit.png" }
                $g.DrawImage($img, $dest, $rect, 'Pixel')
                if (Test-Path $litPath) { $lit = [System.Drawing.Bitmap]::FromFile($litPath); $g.DrawImage($lit, $dest, [System.Drawing.Rectangle]::new(0, 0, 32, 32), 'Pixel'); $lit.Dispose() }
                $col++
            }
            $img.Dispose()
        }
    }
    $bmp.Save($Preview); $bmp.Dispose()
}
foreach ($b in $parts.Values) { $b.Dispose() }
