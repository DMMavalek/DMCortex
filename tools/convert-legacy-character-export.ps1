param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Slug([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    $s = $text.Trim().ToLowerInvariant()
    $s = [regex]::Replace($s, '[^a-z0-9]+', '-')
    $s = $s.Trim('-')
    if ([string]::IsNullOrWhiteSpace($s)) { return 'unknown' }
    return $s
}

function ToCoinsFromGold([decimal]$unitCostGp) {
    $totalCopper = [int][Math]::Round($unitCostGp * 100)
    if ($totalCopper -lt 0) { $totalCopper = 0 }

    $gold = [int][Math]::Floor($totalCopper / 100)
    $remaining = $totalCopper % 100
    $silver = [int][Math]::Floor($remaining / 10)
    $copper = $remaining % 10

    return @{ Gold = $gold; Silver = $silver; Copper = $copper }
}

if (-not (Test-Path $InputPath)) {
    throw "Input file not found: $InputPath"
}

$raw = Get-Content -Raw $InputPath | ConvertFrom-Json
$legacy = $raw.Character
if ($null -eq $legacy) {
    $legacy = $raw
}

if ($null -eq $legacy -or [string]::IsNullOrWhiteSpace($legacy.Name)) {
    throw "Input file does not contain a recognizable legacy character export."
}

$raceName = if ($null -ne $legacy.RaceName) { $legacy.RaceName } else { '' }
$subraceName = if ($null -ne $legacy.SubraceName) { $legacy.SubraceName } else { '' }
$classDisplay = if ($null -ne $legacy.ClassDisplayName) { $legacy.ClassDisplayName } else { '' }

$raceId = Slug $raceName
if (-not [string]::IsNullOrWhiteSpace($subraceName)) {
    $raceId = "$raceId-$(Slug $subraceName)"
}

$classToken = $classDisplay
if ($classToken.Contains('/')) { $classToken = $classToken.Split('/')[0] }
if ($classToken.Contains(',')) { $classToken = $classToken.Split(',')[0] }
$classId = Slug $classToken
if ([string]::IsNullOrWhiteSpace($classId)) { $classId = 'fighter' }

$abilities = @{
    str = 10
    dex = 10
    con = 10
    int = 10
    wis = 10
    cha = 10
}
if ($null -ne $legacy.BaseScores) {
    if ($null -ne $legacy.BaseScores.Strength) { $abilities['str'] = [int]$legacy.BaseScores.Strength }
    if ($null -ne $legacy.BaseScores.Dexterity) { $abilities['dex'] = [int]$legacy.BaseScores.Dexterity }
    if ($null -ne $legacy.BaseScores.Constitution) { $abilities['con'] = [int]$legacy.BaseScores.Constitution }
    if ($null -ne $legacy.BaseScores.Intelligence) { $abilities['int'] = [int]$legacy.BaseScores.Intelligence }
    if ($null -ne $legacy.BaseScores.Wisdom) { $abilities['wis'] = [int]$legacy.BaseScores.Wisdom }
    if ($null -ne $legacy.BaseScores.Charisma) { $abilities['cha'] = [int]$legacy.BaseScores.Charisma }
}

$equipmentSelections = @()
$equipmentNames = @()
if ($null -ne $legacy.EquipmentItems) {
    foreach ($it in @($legacy.EquipmentItems)) {
        if ($null -eq $it) { continue }
        $name = if ($null -ne $it.Name) { [string]$it.Name } else { '' }
        if ([string]::IsNullOrWhiteSpace($name)) { continue }

        $quantity = 1
        if ($null -ne $it.Quantity) { $quantity = [int]$it.Quantity }
        if ($quantity -lt 1) { $quantity = 1 }

        $coins = ToCoinsFromGold ([decimal]$it.UnitCostGp)
        $cat = if ($null -ne $it.Category) { [string]$it.Category } else { '' }
        $isWeapon = $cat -match 'weapon'
        $isArmor = ($cat -match 'armor') -or (([int]$it.ArmorClassModifier) -ne 0)

        $equipmentSelections += [pscustomobject]@{
            ItemId = if ($null -ne $it.ItemId -and -not [string]::IsNullOrWhiteSpace($it.ItemId)) { [string]$it.ItemId } else { Slug $name }
            Category = $cat
            ItemName = $name
            CostText = if ($null -ne $it.UnitCostGp) { "$($it.UnitCostGp) gp" } else { '' }
            Quantity = $quantity
            IsArmor = $isArmor
            ArmorClassValue = 10
            RogueArmorProfile = 'no_armor'
            IsShield = $false
            IsWeapon = $isWeapon
            WeaponSpeed = 0
            WeaponDamageSmallMedium = ''
            WeaponDamageLarge = ''
            WeaponType = ''
            WeaponSize = ''
            CostGoldEach = $coins.Gold
            CostSilverEach = $coins.Silver
            CostCopperEach = $coins.Copper
            SizeClassEach = 'Medium'
            WeightEach = if ($null -ne $it.Weight) { [double]$it.Weight } else { 0 }
        }

        if ($quantity -gt 1) {
            $equipmentNames += "$name x$quantity"
        }
        else {
            $equipmentNames += $name
        }
    }
}

$weaponProficiencies = @()
if ($null -ne $legacy.SelectedWeaponProficiencies) {
    foreach ($wp in @($legacy.SelectedWeaponProficiencies)) {
        if ($null -eq $wp) { continue }
        $weaponName = if ($null -ne $wp.WeaponName) { [string]$wp.WeaponName } else { '' }
        if ([string]::IsNullOrWhiteSpace($weaponName)) { continue }

        $weaponProficiencies += [pscustomobject]@{
            ProficiencyId = Slug $weaponName
            ProficiencyType = 'individual'
            DisplayName = $weaponName
            Specialized = [bool]$wp.IsSpecialized
            WeaponOfChoice = [bool]$wp.IsPreferred
            WeaponExpertise = $false
        }
    }
}

$gp = 0
$sp = 0
$cp = 0
if ($null -ne $legacy.GoldPieces) { $gp += [int]$legacy.GoldPieces }
if ($null -ne $legacy.SilverPieces) { $sp += [int]$legacy.SilverPieces }
if ($null -ne $legacy.CopperPieces) { $cp += [int]$legacy.CopperPieces }
if ($null -ne $legacy.PlatinumPieces) { $gp += ([int]$legacy.PlatinumPieces * 5) }
if ($null -ne $legacy.ElectrumPieces) {
    $ep = [int]$legacy.ElectrumPieces
    $gp += [int][Math]::Floor($ep / 2)
    if (($ep % 2) -ne 0) { $sp += 5 }
}

$notes = @()
$notes += "Imported from legacy export: $([System.IO.Path]::GetFileName($InputPath))"
if (-not [string]::IsNullOrWhiteSpace($legacy.CharacterId)) { $notes += "Legacy CharacterId: $($legacy.CharacterId)" }
if (-not [string]::IsNullOrWhiteSpace($legacy.ClassAbilitiesSummary)) { $notes += "Legacy class abilities: $($legacy.ClassAbilitiesSummary)" }
if (-not [string]::IsNullOrWhiteSpace($legacy.RacialAbilitiesSummary)) { $notes += "Legacy racial abilities: $($legacy.RacialAbilitiesSummary)" }
if (-not [string]::IsNullOrWhiteSpace($legacy.Equipment)) { $notes += "Legacy equipment summary: $($legacy.Equipment)" }

$converted = [ordered]@{
    Name = [string]$legacy.Name
    PlayerName = if ($null -ne $legacy.PlayerName) { [string]$legacy.PlayerName } else { '' }
    Party = if ($null -ne $legacy.PartyName) { [string]$legacy.PartyName } else { '' }
    RaceId = $raceId
    ClassId = $classId
    CharacterMode = 'players_option'
    Level = if ($null -ne $legacy.Level) { [int]$legacy.Level } else { 1 }
    ExperiencePoints = if ($null -ne $legacy.ExperiencePoints) { [int]$legacy.ExperiencePoints } else { 0 }
    GoldPieces = $gp
    SilverPieces = $sp
    CopperPieces = $cp
    BaseHitPoints = if ($null -ne $legacy.HitPoints) { [int]$legacy.HitPoints } else { 1 }
    HitPoints = if ($null -ne $legacy.HitPoints) { [int]$legacy.HitPoints } else { 1 }
    CurrentHitPoints = if ($null -ne $legacy.HitPoints) { [int]$legacy.HitPoints } else { 1 }
    BaseArmorClass = 10
    ArmorClass = 10
    ArmorProfile = 'no_armor'
    Thac0 = 20
    BaseMovement = 12
    Movement = 12
    Revision = 1
    LastModified = (Get-Date).ToString('o')
    Abilities = $abilities
    Notes = $notes
    RacialAbilities = @($legacy.SelectedRacialAbilityNames)
    StructuredAbilities = @()
    Bonuses = @{}
    RacialPointBudget = 0
    RacialPointSpent = 0
    RacialPointRemaining = 0
    ClassPointBudget = 0
    ClassPointSpent = 0
    ClassPointRemaining = 0
    ClassAbilityCarryoverPoints = if ($null -ne $legacy.RacialToClassCarryover) { [int]$legacy.RacialToClassCarryover } else { 0 }
    SelectedRacialAbilityIds = @($legacy.SelectedRacialAbilityNames | ForEach-Object { "${raceId}_$(Slug $_)" })
    SelectedClassAbilityIds = @($legacy.SelectedClassAbilityNames | ForEach-Object { "${classId}_$(Slug $_)" })
    WizardSpecializationId = ''
    NonweaponProficiencies = @($legacy.SelectedNwpNames)
    Languages = @($legacy.KnownLanguages)
    Equipment = $equipmentNames
    Traits = @($legacy.SelectedTraitNames)
    Disadvantages = @($legacy.SelectedDisadvantageNames)
    RaceName = $raceName
    ClassName = $classDisplay
    ClassMode = 'single'
    ClassIds = @($classId)
    NonweaponProficiencyIds = @()
    WeaponProficiencies = $weaponProficiencies
    EquipmentSelections = $equipmentSelections
    UnspentCharacterPoints = if ($null -ne $legacy.UnspentCharacterPoints) { [int]$legacy.UnspentCharacterPoints } else { 0 }
    CpPerLevel = if ($null -ne $legacy.PointsPerLevel) { [int]$legacy.PointsPerLevel } else { 0 }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($InputPath)
    $dir = [System.IO.Path]::GetDirectoryName($InputPath)
    $OutputPath = Join-Path $dir ($baseName + '.dmcortex.import.json')
}

$converted | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 $OutputPath
Write-Host "Converted character written to: $OutputPath"
