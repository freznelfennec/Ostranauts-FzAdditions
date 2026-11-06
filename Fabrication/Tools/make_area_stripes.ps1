#Run from anywhere: powershell -File Fabrication\Tools\make_area_stripes.ps1
param([string]$Out = "$PSScriptRoot\..\..\DataMods\FzAdditions\images\fabrication")
#Builds the working area's hazard stripe textures, one for each state. They're at the game's pixel scale, 16 texels to
#a tile: the band is 4 texels (a quarter tile) across, and the stripes are 4 texels wide at 45 degrees, repeating every
#8 texels along it. FzAreaProjection lays them along the band at that scale, so stripes line up with the game's pixels.
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null

$width = 8; $height = 4; $period = 8
$states = [ordered]@{
    Idle    = @(64, 140, 255, 255)
    Working = @(255, 166, 26, 255)
    Blocked = @(255, 51, 38, 255)
    Preview = @(64, 140, 255, 150)
}
foreach ($state in $states.Keys) {
    $c = $states[$state]
    $bmp = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $on = (($x + $y) % $period) -lt ($period / 2)
            $color = if ($on) { [System.Drawing.Color]::FromArgb($c[3], $c[0], $c[1], $c[2]) } else { [System.Drawing.Color]::FromArgb(0, 0, 0, 0) }
            $bmp.SetPixel($x, $y, $color)
        }
    }
    $bmp.Save("$Out\FzAreaStripe$state.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
