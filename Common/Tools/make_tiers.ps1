#Run from anywhere: powershell -File Common\Tools\make_tiers.ps1
#Writes the Advanced (02) and Superior (03) tiers of the mod's devices from their basic data, so edit the basic
#data or this script, never the *_tiers.json files it writes.
#
#For each family it clones every condowner, mode switch interaction, install job, loot entry and power info whose
#name is the family's, and the install triggers (the ones naming an Uninstalled item), into each tier: names get
#the tier's number, every reference between them is renamed to match, the specs scale, and each form gets the tier's
#condition (IsFzTier2, IsFzTier3), which the tier's install triggers require and the basic ones forbid, so an item
#only ever installs as its own tier. Item definitions, art, panels and the other triggers stay the basic ones'.
#It also writes the tiers' recipes, whose blueprints only Faure-Zhang's data vaults give.
param([string]$Data = "$PSScriptRoot\..\..\DataMods\FzAdditions\data")

$Specs = 1.3 #Each tier's specs over the last
$SiloSpecs = 1.25 #Silos' capacity over the last
$PriceStep = 1.6 #Each tier's price over the last
$Tiers = @(@{ N = 2; Word = 'Advanced'; Code = '02' }, @{ N = 3; Word = 'Superior'; Code = '03' })

#Token: what every name of the family contains, tier digits and all. Bare: the same without them, for names that
#don't carry the tier (the triggers). Specs: the starting conditions to scale, and how.
$Families = @(
    @{ Token = 'FzRecycler'; Bare = 'FzRecycler'; Specs = @{ StatFzAreaMaxWidth = 'round'; StatFzAreaMaxDepth = 'round'; StatFzWorkSpeed = 'tenth' } },
    @{ Token = 'FzFabricator'; Bare = 'FzFabricator'; Specs = @{ StatFzAreaMaxWidth = 'fixed'; StatFzAreaMaxDepth = 'fixed'; StatFzWorkSpeed = 'tenth' }
        Fixed = @{ 2 = @{ StatFzAreaMaxWidth = 5; StatFzAreaMaxDepth = 5 }; 3 = @{ StatFzAreaMaxWidth = 7; StatFzAreaMaxDepth = 7 } } }, #Per the user, odd squares
    @{ Token = 'FzJumpFieldEmitter'; Bare = 'FzJumpFieldEmitter'; Specs = @{ StatFzFieldEnergyMax = 'round'; StatFzFieldSegmentMax = 'tenth'; StatFzFieldOffsetMax = 'tenth' } },
    @{ Token = 'FzSmesReactorTap01'; Bare = 'FzSmesReactorTap'; Specs = @{ StatFzSmesReactorTapChargeRate = 'tenk'; StatFzSmesReactorTapBurnRate = 'fixed' } #To 10,000 kW: 130,000 and 170,000
        #Per the user, 25% and 60% more fuel efficient: reactor burn per kWh delivered, 0.002 on the basic tap, divided by 1.25 and 1.6
        Fixed = @{ 2 = @{ StatFzSmesReactorTapBurnRate = 0.0016 }; 3 = @{ StatFzSmesReactorTapBurnRate = 0.00125 } } },
    @{ Token = 'FzSmes01'; Bare = 'FzSmes'; Specs = @{ StatFzSmesChargeMax = 'fixed'; StatFzSmesChargeRate = 'round' }; Fixed = @{ 2 = @{ StatFzSmesChargeMax = 32000 }; 3 = @{ StatFzSmesChargeMax = 45000 } }; NotBare = 'FzSmesReactorTap' },
    @{ Token = 'FzSiloSmall'; Bare = 'FzSiloSmall'; Specs = @{ StatFzSiloCapacity = 'fixed' }; Fixed = @{ 2 = @{ StatFzSiloCapacity = 6500 }; 3 = @{ StatFzSiloCapacity = 8000 } } } #Per the user, rounded
)

$Folders = 'condowners', 'interactions', 'installables', 'loot', 'powerinfos', 'condtrigs'
$KeepFields = 'strItemDef', 'strPortraitImg', 'strImg', 'strImgNorm', 'strImgDamaged'
$Enc = New-Object Text.UTF8Encoding $false

function Read-Entries($folder) {
    $entries = @()
    foreach ($file in Get-ChildItem "$Data\$folder\fz_*.json" | Where-Object { $_.Name -notmatch '_tiers\.json$' }) {
        $parsed = Get-Content $file.FullName -Raw | ConvertFrom-Json
        foreach ($entry in $parsed) { $entries += $entry }
    }
    return $entries
}

#Whether an entry belongs to a family and is cloned for each tier
function Is-Cloned($family, $folder, $name) {
    if ([string]::IsNullOrEmpty($name) -or $name -match 'GUI') { return $false }
    if ($family.NotBare -and $name.Contains($family.NotBare)) { return $false }
    if (-not ($name.Contains($family.Token) -or $name.Contains($family.Bare))) { return $false }
    if ($folder -eq 'condtrigs') { return $name -match 'Uninstalled' }
    if ($folder -eq 'interactions') { return $name -match '^MS' }
    return $true
}

function Tier-Name($family, $name, $tier) {
    if ($name.Contains($family.Token)) {
        $tiered = if ($family.Token -match '01$') { $family.Token -replace '01$', $tier.Code } else { $family.Token + $tier.Code }
        return $name.Replace($family.Token, $tiered)
    }
    $i = $name.IndexOf($family.Bare)
    return $name.Substring(0, $i + $family.Bare.Length) + $tier.Code + $name.Substring($i + $family.Bare.Length)
}

#Renames every whole name in a string, leaving names that only start with one alone
function Rename-String($text, $map) {
    if ($null -eq $text) { return $text }
    foreach ($old in $map.Keys) {
        if ($text.Contains($old)) { $text = [regex]::Replace($text, "(?<![A-Za-z0-9])$([regex]::Escape($old))(?![A-Za-z0-9])", $map[$old]) }
    }
    return $text
}

function Rename-Value($value, $map) {
    if ($value -is [string]) { return Rename-String $value $map }
    if ($value -is [System.Array]) { return @($value | ForEach-Object { Rename-Value $_ $map }) }
    if ($value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($prop in $value.PSObject.Properties) {
            if ($KeepFields -contains $prop.Name) { continue }
            $prop.Value = Rename-Value $prop.Value $map
        }
    }
    return $value
}

function Scale-Cond($cond, $family, $tier) {
    if ($cond -notmatch '^([A-Za-z0-9]+)=([0-9.]+)x([0-9.eE-]+)$') { return $cond }
    $name = $Matches[1]; $chance = $Matches[2]; $amount = [double]$Matches[3]
    $steps = $tier.N - 1
    if ($name -eq 'StatBasePrice') { return "$name=${chance}x$([math]::Round($amount * [math]::Pow($PriceStep, $steps), 1))" }
    if (-not $family.Specs.ContainsKey($name)) { return $cond }
    switch ($family.Specs[$name]) {
        'round' { $amount = [math]::Round($amount * [math]::Pow($Specs, $steps)) }
        'tenth' { $amount = [math]::Round($amount * [math]::Pow($Specs, $steps), 1) }
        'step' { $amount = [math]::Round($amount * [math]::Pow($Specs, $steps), 2) }
        'tenk' { $amount = [math]::Round($amount * [math]::Pow($Specs, $steps) / 10000) * 10000 }
        'fixed' { $amount = $family.Fixed[$tier.N][$name] }
        'silo' { $amount = [math]::Round($amount * [math]::Pow($SiloSpecs, $steps)) }
    }
    return "$name=${chance}x$($amount.ToString([Globalization.CultureInfo]::InvariantCulture))"
}

function Tier-Text($text, $tier) {
    if ($null -eq $text) { return $text }
    return $text.Replace('(Basic', "($($tier.Word)").Replace('Basic ', "$($tier.Word) ").Replace('A basic ', "An $($tier.Word.ToLower()) ")
}

function Write-Json($path, $entries) {
    $json = ConvertTo-Json -InputObject ([object[]]@($entries | ForEach-Object { $_ })) -Depth 20
    $json = $json -replace '\\u0027', "'" -replace '\\u003c', '<' -replace '\\u003e', '>' -replace '\\u0026', '&'
    [IO.File]::WriteAllText($path, ($json -replace '(?<!\r)\n', "`r`n") + "`r`n", $Enc)
}

$all = @{}
foreach ($folder in $Folders) { $all[$folder] = Read-Entries $folder }

$out = @{}
foreach ($folder in $Folders) { $out[$folder] = New-Object System.Collections.Generic.List[object] }

foreach ($family in $Families) {
    foreach ($tier in $Tiers) {
        $map = @{}
        foreach ($folder in $Folders) {
            foreach ($entry in $all[$folder]) {
                if (Is-Cloned $family $folder $entry.strName) { $map[$entry.strName] = Tier-Name $family $entry.strName $tier }
            }
        }
        foreach ($folder in $Folders) {
            foreach ($entry in $all[$folder]) {
                if (-not (Is-Cloned $family $folder $entry.strName)) { continue }
                $copy = $entry | ConvertTo-Json -Depth 20 | ConvertFrom-Json
                $copy = Rename-Value $copy $map
                if ($folder -eq 'condowners') {
                    $copy.aStartingConds = @(@($copy.aStartingConds | ForEach-Object { Scale-Cond $_ $family $tier }) + "IsFzTier$($tier.N)=1.0x1")
                    $copy.strNameFriendly = Tier-Text $copy.strNameFriendly $tier
                    if ($copy.strNameShort) { $copy.strNameShort = Tier-Text $copy.strNameShort $tier }
                    if ($copy.strDesc) { $copy.strDesc = Tier-Text $copy.strDesc $tier }
                }
                if ($folder -eq 'condtrigs') {
                    $copy.aReqs = @(@($copy.aReqs) + "IsFzTier$($tier.N)")
                    $copy.aForbids = @(@($copy.aForbids) | Where-Object { $_ -notmatch '^IsFzTier' })
                }
                $out[$folder].Add($copy)
            }
        }
    }
}

foreach ($folder in $Folders) {
    Write-Json "$Data\$folder\fz_${folder}_tiers.json" $out[$folder]
    Write-Host "$folder`: $($out[$folder].Count) entries"
}

#Recipes: each tier's materials are the last's times the spec step, with one more unit of Exotics, so only
#Faure-Zhang's refinery output makes them. Blueprints are the ones named in Faure-Zhang's vault list.
$base = @{
    FzRecycler = @{ Name = 'Recycler'; Category = 'Fabrication.Machines'; Loose = 'ItmFzRecycler{0}Loose'; Forms = 'ItmFzRecycler{0}', 'ItmFzRecycler{0}Off'; Dmg = 'ItmFzRecycler{0}Dmg', 'ItmFzRecycler{0}DmgLoose'
        Materials = [ordered]@{ BaseMetals = 120; MetalAlloys = 60; PreciousMetals = 14; RareEarths = 10; Polymers = 30; Silicates = 24; Ceramics = 16; Exotics = 0 } }
    FzFabricator = @{ Name = 'Fabricator'; Category = 'Fabrication.Machines'; Loose = 'ItmFzFabricator{0}Loose'; Forms = 'ItmFzFabricator{0}', 'ItmFzFabricator{0}Off'; Dmg = 'ItmFzFabricator{0}Dmg', 'ItmFzFabricator{0}DmgLoose'
        Materials = [ordered]@{ BaseMetals = 110; MetalAlloys = 70; PreciousMetals = 18; RareEarths = 14; Polymers = 26; Silicates = 30; Ceramics = 20; Exotics = 0 } }
    FzSiloSmall = @{ Name = 'Small Silo'; Category = 'Fabrication.Storage'; Loose = 'ItmFzSiloSmall{0}Loose'; Forms = @('ItmFzSiloSmall{0}'); Dmg = 'ItmFzSiloSmall{0}Dmg', 'ItmFzSiloSmall{0}DmgLoose'
        Materials = [ordered]@{ BaseMetals = 40; MetalAlloys = 12; Polymers = 8; Ceramics = 4; Exotics = 0 } }
}
$recipes = @{}
$parsedRecipes = Get-Content "$Data\fz\fabrication\recipes\fz_manual_fztech.json" -Raw | ConvertFrom-Json
foreach ($r in $parsedRecipes) { $recipes[$r.strName] = $r }
foreach ($pair in @(@('FzSmes01', 'ItmFzSmes01', 'SMES Unit', $false), @('FzSmesReactorTap01', 'ItmFzSmesReactorTap01', 'SMES Reactor Tap', $true), @('FzJumpFieldEmitter', 'ItmFzJumpFieldEmitter', 'Jump Drive Field Emitter', $true))) {
    $r = $recipes[$pair[1]]
    if ($null -eq $r) { Write-Host "No basic recipe $($pair[1])"; continue }
    $stem = $pair[1] -replace '01$', ''
    $materials = [ordered]@{}; foreach ($p in $r.mapMaterials.PSObject.Properties) { $materials[$p.Name] = [int]$p.Value }
    $forms = if ($pair[3]) { @('{0}', '{0}Off') } else { @('{0}') }
    $base[$pair[0]] = @{ Name = $pair[2]; Category = $r.strCategory; Loose = "$stem{0}Loose"; Forms = @($forms | ForEach-Object { $_ -f "$stem{0}" }); Dmg = "$stem{0}Dmg", "$stem{0}DmgLoose"; Materials = $materials; Scaled = $true }
}

$recipeOut = New-Object System.Collections.Generic.List[object]
foreach ($key in $base.Keys) {
    $b = $base[$key]
    foreach ($tier in $Tiers) {
        $steps = if ($b.Scaled) { $tier.N - 1 } else { $tier.N - 2 }
        $materials = [ordered]@{}
        foreach ($m in $b.Materials.Keys) {
            $amount = if ($m -eq 'Exotics') { $b.Materials[$m] + $tier.N - 1 } else { [math]::Ceiling($b.Materials[$m] * [math]::Pow($Specs, $steps)) }
            if ($amount -gt 0) { $materials[$m] = [int]$amount }
        }
        $stemCode = $tier.Code
        $fabricate = $b.Loose -f $stemCode
        $recipeOut.Add([pscustomobject][ordered]@{
            strName = ($b.Forms[0] -f $stemCode); strNameFriendly = "$($b.Name) ($($tier.Word))"; strCategory = $b.Category
            aCOs = @(@($fabricate) + @($b.Forms | ForEach-Object { $_ -f $stemCode })); strCOFabricate = $fabricate
            mapMaterials = $materials; bFabricate = $true; bRecycle = $true; fRecycleYield = 0.5
            strBlueprint = ($key -replace '01$', '') + $stemCode; strTier = 'Exotic'
        })
        $half = [ordered]@{}; foreach ($m in $materials.Keys) { if ($m -ne 'Exotics') { $half[$m] = [int][math]::Ceiling($materials[$m] / 2) } }
        $recipeOut.Add([pscustomobject][ordered]@{
            strName = ($b.Dmg[0] -f $stemCode); strNameFriendly = "$($b.Name) ($($tier.Word))"; strCategory = $b.Category
            aCOs = @($b.Dmg | ForEach-Object { $_ -f $stemCode }); mapMaterials = $half; bFabricate = $false; bRecycle = $true; fRecycleYield = 0.5; strTier = 'Exotic'
        })
    }
}
Write-Json "$Data\fz\fabrication\recipes\fz_manual_tiers.json" $recipeOut
Write-Host "recipes: $($recipeOut.Count)"
