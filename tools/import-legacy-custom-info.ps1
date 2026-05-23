$ErrorActionPreference = 'Stop'

$root = 'C:\Users\kelava\Documents\Projects\Dungeon Master Cortex'
$customDir = Join-Path $root 'Assets\Custom Info'
$corePath = Join-Path $root 'data\rulesets\core_2e.json'

function Slug([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    $s = $text.ToLowerInvariant()
    $s = [regex]::Replace($s, '[^a-z0-9]+', '-')
    $s = $s.Trim('-')
    if ([string]::IsNullOrWhiteSpace($s)) { return 'custom-entry' }
    return $s
}

function AbilityKey([string]$name) {
    $v = ''
    if ($null -ne $name) { $v = $name.Trim().ToLowerInvariant() }
    switch -Regex ($v) {
        '^str|strength$' { return 'str' }
        '^dex|dexterity$' { return 'dex' }
        '^con|constitution$' { return 'con' }
        '^int|intelligence$' { return 'int' }
        '^wis|wisdom$' { return 'wis' }
        '^cha|charisma$' { return 'cha' }
        default { return '' }
    }
}

function ParseFlatAbilityModifiers([string]$text) {
    $result = @{}
    if ([string]::IsNullOrWhiteSpace($text)) { return $result }
    $abilityMatches = [regex]::Matches($text, '(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma)\s*:\s*([+-]?\d+)', 'IgnoreCase')
    foreach ($m in $abilityMatches) {
        $k = AbilityKey $m.Groups[1].Value
        if (-not [string]::IsNullOrWhiteSpace($k)) {
            $v = [int]$m.Groups[2].Value
            if ($result.ContainsKey($k)) { $result[$k] += $v } else { $result[$k] = $v }
        }
    }
    return $result
}

function ModeFromRuleset([string]$ruleset) {
    $v = ''
    if ($null -ne $ruleset) { $v = $ruleset.Trim().ToLowerInvariant() }
    if ($v -eq 'players option') { return 'players_option' }
    if ($v -eq 'core') { return 'core_rules' }
    return 'all'
}

function MapSaveCategory([string]$value) {
    $v = ''
    if ($null -ne $value) { $v = $value.Trim().ToLowerInvariant() }
    switch -Regex ($v) {
        '^all$' { return 'all' }
        'poison' { return 'poison' }
        'spell|magic' { return 'magic' }
        'breath' { return 'breath' }
        'petrif|polymorph' { return 'petrify' }
        'rod|staff|wand' { return 'rod_staff_wand' }
        default { return '' }
    }
}

function MapNwpToSubAbility([string]$value) {
    $v = ''
    if ($null -ne $value) { $v = $value.Trim().ToLowerInvariant() }
    switch -Regex ($v) {
        'pick\s*pockets' { return 'pick_pockets' }
        'open\s*locks' { return 'open_locks' }
        'move\s*silently' { return 'move_silently' }
        'hide\s*in\s*shadows' { return 'hide_in_shadows' }
        'climb\s*walls' { return 'climb_walls' }
        default { return '' }
    }
}

function ParseLegacyMechanicalRules([string]$mechanicalRulesJson, [string]$effectText, [string]$description) {
    $mechanics = @{}
    $saveBonuses = @{}
    $subBonuses = @{}

    if (-not [string]::IsNullOrWhiteSpace($mechanicalRulesJson)) {
        try {
            $rules = ConvertFrom-Json -InputObject $mechanicalRulesJson
            $rows = @($rules)
            foreach ($row in $rows) {
                if ($null -eq $row) { continue }

                $ruleType = ''
                if ($null -ne $row.ruleType) { $ruleType = $row.ruleType.ToString().Trim() }
                $appliesKind = ''
                if ($null -ne $row.appliesToKind) { $appliesKind = $row.appliesToKind.ToString().Trim() }
                $appliesValue = ''
                if ($null -ne $row.appliesToValue) { $appliesValue = $row.appliesToValue.ToString().Trim() }

                $baseValue = 0
                if ($null -ne $row.baseValue) { $baseValue = [int]$row.baseValue }

                if ([string]::Equals($ruleType, 'SaveBonusFlat', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if ([string]::Equals($appliesKind, 'SaveCategory', [System.StringComparison]::OrdinalIgnoreCase)) {
                        $saveKey = MapSaveCategory $appliesValue
                        if (-not [string]::IsNullOrWhiteSpace($saveKey)) {
                            if ($saveBonuses.ContainsKey($saveKey)) { $saveBonuses[$saveKey] += $baseValue } else { $saveBonuses[$saveKey] = $baseValue }
                        }
                    }
                    continue
                }

                if ([string]::Equals($ruleType, 'NwpScoreBonusFlat', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if ([string]::Equals($appliesKind, 'NwpName', [System.StringComparison]::OrdinalIgnoreCase)) {
                        $subKey = MapNwpToSubAbility $appliesValue
                        if (-not [string]::IsNullOrWhiteSpace($subKey)) {
                            if ($subBonuses.ContainsKey($subKey)) { $subBonuses[$subKey] += $baseValue } else { $subBonuses[$subKey] = $baseValue }
                        }
                    }
                    continue
                }
            }
        }
        catch {
            # Keep import resilient if this one ability has malformed legacy JSON.
        }
    }

    if ($saveBonuses.Count -eq 0) {
        $combinedText = "$description $effectText"
        if ($combinedText -match '(?i)(save|saving throw).*(spell|magic)') {
            $plusMatches = [regex]::Matches($combinedText, '(?i)([+-]\d+)')
            if ($plusMatches.Count -gt 0) {
                $inferred = [int]$plusMatches[0].Groups[1].Value
                $saveBonuses['magic'] = $inferred
            }
        }
    }

    if ($subBonuses.Count -eq 0) {
        $combinedText = "$description $effectText"
        $mHide = [regex]::Match($combinedText, '(?i)([+-]\d+)\s*%\s*to\s*hide\s*in\s*shadows')
        if ($mHide.Success) {
            $subBonuses['hide_in_shadows'] = [int]$mHide.Groups[1].Value
        }

        $mMove = [regex]::Match($combinedText, '(?i)([+-]\d+)\s*%\s*to\s*move\s*silently')
        if ($mMove.Success) {
            $existing = 0
            if ($subBonuses.ContainsKey('move_silently')) { $existing = [int]$subBonuses['move_silently'] }
            $subBonuses['move_silently'] = $existing + [int]$mMove.Groups[1].Value
        }
    }

    if ($saveBonuses.Count -gt 0) {
        $mechanics['save_bonuses'] = [pscustomobject]$saveBonuses
    }
    if ($subBonuses.Count -gt 0) {
        $mechanics['subability_bonuses'] = [pscustomobject]$subBonuses
    }

    return [pscustomobject]$mechanics
}

function Add-PropertyIfMissing([object]$obj, [string]$name, [object]$value) {
    if (-not ($obj.PSObject.Properties.Name -contains $name)) {
        $obj | Add-Member -NotePropertyName $name -NotePropertyValue $value
    }
}

if (-not (Test-Path $customDir)) { throw "Missing folder: $customDir" }
if (-not (Test-Path $corePath)) { throw "Missing file: $corePath" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$coreBackup = "$corePath.$stamp.bak"
Copy-Item $corePath $coreBackup -Force

$core = Get-Content -Raw $corePath | ConvertFrom-Json
Add-PropertyIfMissing $core 'races' ([pscustomobject]@{})
Add-PropertyIfMissing $core 'classes' ([pscustomobject]@{})

$raceFiles = Get-ChildItem $customDir -Filter 'custom-races*.json' -File | Sort-Object Name
$classFiles = Get-ChildItem $customDir -Filter 'custom-classes*.json' -File | Sort-Object Name

$importedRaces = @()
$seenRaceIds = @{}

foreach ($f in $raceFiles) {
    $obj = Get-Content -Raw $f.FullName | ConvertFrom-Json
    $raceList = @($obj.Races)
    $allAbilities = @($obj.RacialAbilities)

    foreach ($r in $raceList) {
        $id = Slug $r.Name
        if ([string]::IsNullOrWhiteSpace($id)) { continue }

        $abilityModifiers = ParseFlatAbilityModifiers $r.AbilityModifiersText

        $abilitiesOut = @()
        $index = 0
        foreach ($ab in $allAbilities | Where-Object { $_.RaceName -eq $r.Name }) {
            $index++
            $subrace = ''
            if ($null -ne $ab.SubraceName) { $subrace = $ab.SubraceName.Trim() }

            $descParts = @()
            if (-not [string]::IsNullOrWhiteSpace($subrace)) { $descParts += "Subrace: $subrace" }
            if (-not [string]::IsNullOrWhiteSpace($ab.Description)) { $descParts += $ab.Description.Trim() }
            if (-not [string]::IsNullOrWhiteSpace($ab.EffectText)) { $descParts += "Effect: $($ab.EffectText.Trim())" }
            if (-not [string]::IsNullOrWhiteSpace($ab.SourceBook)) { $descParts += "Source: $($ab.SourceBook.Trim())" }

            $desc = ($descParts -join ' | ')
            if ([string]::IsNullOrWhiteSpace($desc)) {
                if ([string]::IsNullOrWhiteSpace($ab.Name)) { $desc = "Ability $index" } else { $desc = $ab.Name }
            }

            $abilityNamePart = $ab.Name
            if ([string]::IsNullOrWhiteSpace($abilityNamePart)) { $abilityNamePart = "ability-$index" }
            $abilityId = "${id}_$(Slug ($subrace + '-' + $abilityNamePart))"

            $cost = 0
            if ($null -ne $ab.Cost) { $cost = [int]$ab.Cost }

            $effectText = ''
            if ($null -ne $ab.EffectText) { $effectText = $ab.EffectText }
            $mechanics = ParseLegacyMechanicalRules $ab.MechanicalRulesJson $effectText $desc

            $abilitiesOut += [pscustomobject]@{
                id = $abilityId
                description = $desc
                point_cost = $cost
                auto_granted = $false
                mechanics = $mechanics
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($r.SubracesText)) {
            $abilitiesOut += [pscustomobject]@{
                id = "${id}_subraces"
                description = "Subraces: $($r.SubracesText.Trim())"
                point_cost = 0
                auto_granted = $true
                mechanics = [pscustomobject]@{}
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($r.AllowedClassesText)) {
            $abilitiesOut += [pscustomobject]@{
                id = "${id}_allowed_classes"
                description = "Allowed classes: $($r.AllowedClassesText.Trim())"
                point_cost = 0
                auto_granted = $true
                mechanics = [pscustomobject]@{}
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($r.AllowedMultiClassCombinationsText)) {
            $abilitiesOut += [pscustomobject]@{
                id = "${id}_multiclass_notes"
                description = "Allowed multiclass combinations: $($r.AllowedMultiClassCombinationsText.Trim())"
                point_cost = 0
                auto_granted = $true
                mechanics = [pscustomobject]@{}
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($r.SubraceAbilityModifiersText)) {
            $abilitiesOut += [pscustomobject]@{
                id = "${id}_subrace_ability_modifiers"
                description = "Subrace ability modifiers: $($r.SubraceAbilityModifiersText.Trim())"
                point_cost = 0
                auto_granted = $true
                mechanics = [pscustomobject]@{}
            }
        }

        $pointBudget = 0
        if ($null -ne $r.RacialPointBudget) { $pointBudget = [int]$r.RacialPointBudget }

        $raceName = $id
        if (-not [string]::IsNullOrWhiteSpace($r.Name)) { $raceName = $r.Name }

        $raceObj = [pscustomobject]@{
            name = $raceName
            source = 'custom'
            character_mode = (ModeFromRuleset $r.Ruleset)
            base_race_id = $id
            ability_minimums = [pscustomobject]@{}
            ability_maximums = [pscustomobject]@{}
            ability_modifiers = [pscustomobject]$abilityModifiers
            racial_abilities = $abilitiesOut
            racial_point_budget = $pointBudget
        }

        $core.races | Add-Member -NotePropertyName $id -NotePropertyValue $raceObj -Force

        if (-not $seenRaceIds.ContainsKey($id)) {
            $seenRaceIds[$id] = $true
            $importedRaces += "$id ($($r.Name))"
        }
    }
}

$importedClasses = @()
$seenClassIds = @{}

foreach ($f in $classFiles) {
    $obj = Get-Content -Raw $f.FullName | ConvertFrom-Json
    foreach ($c in @($obj.Classes)) {
        $id = Slug $c.Name
        if ([string]::IsNullOrWhiteSpace($id)) { continue }

        $mins = @{}
        if ($null -ne $c.MinStr -and [int]$c.MinStr -gt 0) { $mins['str'] = [int]$c.MinStr }
        if ($null -ne $c.MinDex -and [int]$c.MinDex -gt 0) { $mins['dex'] = [int]$c.MinDex }
        if ($null -ne $c.MinCon -and [int]$c.MinCon -gt 0) { $mins['con'] = [int]$c.MinCon }
        if ($null -ne $c.MinInt -and [int]$c.MinInt -gt 0) { $mins['int'] = [int]$c.MinInt }
        if ($null -ne $c.MinWis -and [int]$c.MinWis -gt 0) { $mins['wis'] = [int]$c.MinWis }
        if ($null -ne $c.MinCha -and [int]$c.MinCha -gt 0) { $mins['cha'] = [int]$c.MinCha }

        $allowed = @('human','elf','dwarf','gnome','halfling','half-elf','half-orc','half-ogre')

        $classAbilities = @()
        $notesText = ''
        if ($null -ne $c.Notes) { $notesText = ($c.Notes.ToString().Replace("`r", ' ').Replace("`n", ' ').Trim()) }
        if (-not [string]::IsNullOrWhiteSpace($notesText)) {
            $classAbilities += [pscustomobject]@{
                id = "${id}_import_notes"
                description = "Imported class notes: $notesText"
                point_cost = 0
                auto_granted = $true
                mechanics = [pscustomobject]@{}
            }
        }

        foreach ($row in @($c.LevelDetails)) {
            $level = 0
            if ($null -ne $row.Level) { $level = [int]$row.Level }
            foreach ($b in @($row.Bonuses)) {
                $label = if ([string]::IsNullOrWhiteSpace($b.Name)) { 'Bonus' } else { $b.Name.Trim() }
                $cat = if ([string]::IsNullOrWhiteSpace($b.Category)) { 'Ability' } else { $b.Category.Trim() }
                $spellType = if ($null -eq $b.SpellType) { '' } else { $b.SpellType.Trim() }
                $notes = if ($null -eq $b.Notes) { '' } else { $b.Notes.Trim() }

                $desc = ("Level {0}: [{1}] {2}" -f $level, $cat, $label)
                if (-not [string]::IsNullOrWhiteSpace($spellType)) { $desc += " ($spellType)" }
                if (-not [string]::IsNullOrWhiteSpace($notes)) { $desc += " - $notes" }

                $classAbilities += [pscustomobject]@{
                    id = "${id}_lvl${level}_$(Slug "$cat-$label")"
                    description = $desc
                    point_cost = 0
                    auto_granted = $true
                    mechanics = [pscustomobject]@{}
                }
            }
        }

        $budget = 0
        if ($null -ne $c.StartingCharacterPoints) { $budget = [int]$c.StartingCharacterPoints }

        $className = $id
        if (-not [string]::IsNullOrWhiteSpace($c.Name)) { $className = $c.Name }

        $classObj = [pscustomobject]@{
            name = $className
            source = 'custom'
            ability_minimums = [pscustomobject]$mins
            allowed_races = $allowed
            class_abilities = $classAbilities
            class_point_budget = $budget
        }

        $core.classes | Add-Member -NotePropertyName $id -NotePropertyValue $classObj -Force

        if (-not $seenClassIds.ContainsKey($id)) {
            $seenClassIds[$id] = $true
            $importedClasses += "$id ($($c.Name))"
        }
    }
}

$core | ConvertTo-Json -Depth 100 | Set-Content -Encoding UTF8 $corePath

Write-Host "Backup: $coreBackup"
Write-Host "Imported races: $($importedRaces -join ', ')"
Write-Host "Imported classes: $($importedClasses -join ', ')"





