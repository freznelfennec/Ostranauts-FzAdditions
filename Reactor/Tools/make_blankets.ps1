#Run from anywhere: powershell -File Reactor\Tools\make_blankets.ps1
param([string]$Src = "C:\Program Files (x86)\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\images\ItmFusionABLWall01Loose.png",
    [string]$Out = "$PSScriptRoot\..\..\DataMods\FzAdditions\images\reactor")
#Breeder blanket sprites, recoloured from the base game's ablative core liner so they look distinct (see
#Design/Reactor/BreederBlankets.md). The liner's slate blue body becomes copper brown and its teal stripe takes the
#tier's colour, in the mod's material colours. The shape is unchanged, so they use the liner's own normal map.
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

function HsvToColor($a, $h, $s, $v) {
    $h = ($h % 360 + 360) % 360; $c = $v * $s; $x = $c * (1 - [math]::Abs((($h / 60) % 2) - 1)); $m = $v - $c
    $r, $g, $b = switch ([math]::Floor($h / 60)) { 0 { $c, $x, 0 } 1 { $x, $c, 0 } 2 { 0, $c, $x } 3 { 0, $x, $c } 4 { $x, 0, $c } default { $c, 0, $x } }
    [System.Drawing.Color]::FromArgb($a, [int](($r + $m) * 255), [int](($g + $m) * 255), [int](($b + $m) * 255))
}

#Body is the liner's slate blue (hue 195 to 230), the stripe its teal (150 to 185). Everything else is kept.
function Recolor($name, $bodyHue, $bodySat, $stripeHue, $stripeSat, $stripeBoost) {
    $b = [System.Drawing.Bitmap]::FromFile($Src); $o = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    for ($y = 0; $y -lt $b.Height; $y++) { for ($x = 0; $x -lt $b.Width; $x++) {
        $c = $b.GetPixel($x, $y)
        if ($c.A -eq 0) { $o.SetPixel($x, $y, $c); continue }
        $h = $c.GetHue(); $v = [math]::Max($c.R, [math]::Max($c.G, $c.B)) / 255.0
        if ($h -ge 150 -and $h -le 185) { $o.SetPixel($x, $y, (HsvToColor $c.A $stripeHue $stripeSat ([math]::Min(1, $v * $stripeBoost)))) }
        elseif ($h -ge 195 -and $h -le 230) { $o.SetPixel($x, $y, (HsvToColor $c.A $bodyHue $bodySat $v)) }
        else { $o.SetPixel($x, $y, $c) }
    } }
    $o.Save("$Out\$name.png", [System.Drawing.Imaging.ImageFormat]::Png); $o.Dispose(); $b.Dispose()
}

Recolor 'FzBreederBlanket01' 28 0.38 197 0.55 1.25 #Ice blue, the Volatiles colour
Recolor 'FzBreederBlanket02' 28 0.38 38 0.62 1.3   #Amber, the He-3 colour
Recolor 'FzBreederBlanket03' 28 0.38 312 0.62 1.3  #Magenta, the Exotics colour
Recolor 'FzBreederBlanketSpent' 28 0.08 0 0 0.55   #Greyed, with a dark stripe
