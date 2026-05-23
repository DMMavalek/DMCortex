using System;
using System.Collections.Generic;

namespace DungeonMasterCortex.Models;

// ── Mechanical effects attached to a single ability ──────────────────────────

/// <summary>
/// All numeric bonuses a single racial or class ability grants.
/// Fields left at their default (0 / false) have no effect.
/// </summary>
public class AbilityEffect
{
    // Armor Class: negative = better (AD&D convention)
    public int    AcBonus             { get; set; } = 0;

    // If true, AcBonus applies only while unarmored.
    public bool   AcBonusRequiresNoArmor { get; set; } = false;

    // Flat attack-roll bonus (THAC0 reduction)
    public int    AttackBonus         { get; set; } = 0;

    // Flat bonus to weapon damage rolls
    public int    DamageBonus         { get; set; } = 0;

    // Base movement bonus (applied before any encumbrance adjustments)
    public int    MovementBonus       { get; set; } = 0;

    // Saving throw modifiers keyed by save category
    // keys: "poison", "magic", "breath", "petrify", "rod_staff_wand", "all"
    public Dictionary<string, int> SaveBonuses { get; set; } = new();

    // XP modifier as a percentage (e.g. +10 means +10 %)
    public int    XpModifierPercent   { get; set; } = 0;

    // Extra HP gained per level (e.g. +1 for tough hide)
    public int    HpPerLevel          { get; set; } = 0;

    // One-time flat HP bonus applied at character creation
    public int    HpFlatBonus         { get; set; } = 0;

    // Dice expression for HP rolling (e.g., "1d12", "2d6+1")
    // If provided, this replaces the class hit die at level 1
    public string? HpDiceExpression    { get; set; } = null;

    // NWP (nonweapon proficiency) slots granted for free
    public int    NwpSlotBonus        { get; set; } = 0;

    // NWP cost reduction (in slots) when buying certain NWPs
    public int    NwpCostReduction    { get; set; } = 0;

    // NWP check modifier (bonus to NWP ability score rolls)
    public int    NwpCheckBonus       { get; set; } = 0;

    // Surprise roll modifier
    public int    SurpriseBonus       { get; set; } = 0;

    // Subability modifiers keyed by subability name (fitness, stamina, muscle, health, balance, etc.)
    public Dictionary<string, int> SubAbilityBonuses { get; set; } = new();

    // Infravision range in feet (0 = none)
    public int    InfravisionFeet     { get; set; } = 0;

    // Magic resistance as percentage (e.g. 90 for elves vs sleep/charm)
    public int    MagicResistPercent  { get; set; } = 0;

    // Specific enemy attack/damage bonuses keyed by enemy type
    // keys: "goblinoids", "giants", "orcs", "drow", "troglodytes"
    public Dictionary<string, int> EnemyAttackBonuses  { get; set; } = new();
    public Dictionary<string, int> EnemyDamageBonuses  { get; set; } = new();

    // Specific weapon attack bonuses keyed by weapon id/group
    public Dictionary<string, int> WeaponAttackBonuses { get; set; } = new();

    // Specific weapon damage bonuses keyed by weapon id/group
    public Dictionary<string, int> WeaponDamageBonuses { get; set; } = new();

    // Reaction roll modifier with other NPCs/races
    public int    ReactionBonus       { get; set; } = 0;

    // Stealth: true if the ability grants a hide/move-silently bonus
    public bool   GrantsStealth       { get; set; } = false;

    // Detect secret doors: true if the race/class can notice them passively
    public bool   DetectSecretDoors   { get; set; } = false;

    // Detect stonework traps/underground features
    public bool   DetectStonework     { get; set; } = false;
}

// ── Structured ability definition ────────────────────────────────────────────

/// <summary>
/// A single racial or class ability with both its display text and its
/// full mechanical effect record.
/// </summary>
public class AbilityDefinition
{
    public string         Id          { get; set; } = "";
    public string         Description { get; set; } = "";
    public string         Category    { get; set; } = "";   // for grouping/sorting in UI
    public int            PointCost   { get; set; } = 0;
    public bool           AutoGranted { get; set; } = true;
    public bool           AllowMultiple { get; set; } = false;
    public bool           RequiresPlayerText { get; set; } = false;
    public bool           AllowPurchaseAfterLevelOne { get; set; } = false;
    public AbilityEffect  Effect      { get; set; } = new();
}

// ── Aggregated bonuses on a built character ───────────────────────────────────

/// <summary>
/// The net rolled-up bonuses calculated from all of a character's racial
/// and class abilities.  Stored alongside the CharacterSheet so the UI
/// and combat system can read computed values without re-running the engine.
/// </summary>
public class AbilityBonuses
{
    public int    AcBonus             { get; set; } = 0;
    public int    AttackBonus         { get; set; } = 0;
    public int    DamageBonus         { get; set; } = 0;
    public int    MovementBonus       { get; set; } = 0;
    public Dictionary<string, int> SaveBonuses         { get; set; } = new();
    public int    XpModifierPercent   { get; set; } = 0;
    public int    HpPerLevel          { get; set; } = 0;
    public int    HpFlatBonus         { get; set; } = 0;
    public int    NwpSlotBonus        { get; set; } = 0;
    public int    NwpCostReduction    { get; set; } = 0;
    public int    NwpCheckBonus       { get; set; } = 0;
    public int    SurpriseBonus       { get; set; } = 0;
    public Dictionary<string, int> SubAbilityBonuses { get; set; } = new();
    public int    InfravisionFeet     { get; set; } = 0;
    public int    MagicResistPercent  { get; set; } = 0;
    public int    ReactionBonus       { get; set; } = 0;
    public bool   GrantsStealth       { get; set; } = false;
    public bool   DetectSecretDoors   { get; set; } = false;
    public bool   DetectStonework     { get; set; } = false;
    public Dictionary<string, int> EnemyAttackBonuses  { get; set; } = new();
    public Dictionary<string, int> EnemyDamageBonuses  { get; set; } = new();
    public Dictionary<string, int> WeaponAttackBonuses { get; set; } = new();
    public Dictionary<string, int> WeaponDamageBonuses { get; set; } = new();
}

// ── Race / Class definitions ─────────────────────────────────────────────────

public record RaceDefinition(
    string Id,
    string Name,
    string CharacterMode,
    string BaseRaceId,
    Dictionary<string, int>    AbilityMinimums,
    Dictionary<string, int>    AbilityMaximums,
    Dictionary<string, int>    AbilityModifiers,      // racial ability adjustments (e.g., +1 CON, -1 CHA)
    List<string>               RacialAbilities,       // kept for legacy display
    List<AbilityDefinition>    StructuredAbilities,   // new mechanical definitions
    int                        RacialPointBudget = 0,
    string                     Source = "core"
)
{
    public bool IsCustom => string.Equals(Source, "custom", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A wizard specialization (Abjurer, Illusionist, etc.) that auto-selects school abilities
/// and adjusts the CP budget when chosen during character generation.
/// </summary>
public record WizardSpecialization(
    string Id,
    string Name,
    string Description,
    Dictionary<string, int> AbilityMinimums,
    List<string>            AllowedRaces,
    int                     ClassPointBudget,
    List<string>            AutoSelectAbilityIds,
    List<string>            OppositionSchools
);

public record ClassDefinition(
    string Id,
    string Name,
    Dictionary<string, int> AbilityMinimums,
    List<string>            AllowedRaces,
    List<AbilityDefinition> StructuredAbilities,   // class abilities with mechanics
    int                     ClassPointBudget = 0,
    List<WizardSpecialization>? Specializations = null,
    string                  Source = "core",
    string                  RulesMode = "all"
)
{
    public bool IsCustom => string.Equals(Source, "custom", StringComparison.OrdinalIgnoreCase);
}

public record RogueSkillBreakdown(
    string SkillId,
    string SkillName,
    int BaseScore,
    int RacialAdjustment,
    int DexterityAdjustment,
    int ArmorAdjustment,
    int AllocatedPoints,
    int FinalScore
);

public record NonweaponProficiencyDefinition(
    string Id,
    string Name,
    string Category,
    int Slots,
    string CheckAbility,
    int CheckModifier,
    string Description,
    string Source = "Core",
    bool AllowMultiple = false,
    bool RequiresPlayerText = false,
    int CpCost = 0,
    int PlayersOptionBaseRating = 0,
    string PlayersOptionCheckAbility = "",
    IReadOnlyList<string>? AllowedClasses = null,
    string GroupFamily = "",
    string ProficiencyGroup = "",
    bool IsMultiGroup = false,
    string GroupsFound = "",
    string PlayersOptionRaw = "",
    string SourceBook = "",
    string SourceTag = "",
    string SettingName = "",
    string Origin = "",
    string DescriptionPreview = "",
    bool HasDescription = false
);

public class NonweaponProficiencySetting
{
    public string Id { get; set; } = "";
    public bool AllowMultiple { get; set; }
    public bool RequiresPlayerText { get; set; }
}

public class CustomNwpData
{
    public string Id          { get; set; } = "";
    public string Name        { get; set; } = "";
    public string Category    { get; set; } = "General";
    public int    Slots       { get; set; } = 1;
    public string CheckAbility  { get; set; } = "Intelligence";
    public int    CheckModifier { get; set; } = 0;
    public string Description { get; set; } = "";
    public string Source      { get; set; } = "Custom";
    public int    CpCost      { get; set; } = 0;
    public int    PlayersOptionBaseRating { get; set; } = 0;
    public string PlayersOptionCheckAbility { get; set; } = "";
    public List<string> AllowedClasses { get; set; } = new();
    public string GroupFamily { get; set; } = "";
    public string ProficiencyGroup { get; set; } = "";
    public bool   IsMultiGroup { get; set; }
    public string GroupsFound { get; set; } = "";
    public string PlayersOptionRaw { get; set; } = "";
    public string SourceBook { get; set; } = "";
    public string SourceTag { get; set; } = "";
    public string SettingName { get; set; } = "";
    public string Origin { get; set; } = "";
    public string DescriptionPreview { get; set; } = "";
    public bool   HasDescription { get; set; }
}

public class CustomTraitData
{
    public string Id          { get; set; } = "";
    public string Name        { get; set; } = "";
    public int    Cost        { get; set; } = 0;
    public string Description { get; set; } = "";
}

public class CustomDisadvantageData
{
    public string Id          { get; set; } = "";
    public string Name        { get; set; } = "";
    public int    ModerateBonus { get; set; } = 0;
    public int?   SevereBonus   { get; set; }
    public string Description { get; set; } = "";
}

public class CustomEquipmentData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsMagical { get; set; } = false;
    public List<string> Categories { get; set; } = new();
    public List<string> ItemTags { get; set; } = new();
    public string SizeClass { get; set; } = "Medium";
    public double Weight { get; set; } = 0;

    // Armor metadata (descending AC system where lower values are better).
    public bool IsArmor { get; set; } = false;
    public int ArmorClassValue { get; set; } = 10;
    public string RogueArmorProfile { get; set; } = "no_armor";
    // Shield metadata: IsShield=true means this item adds an AC bonus on top of body armor.
    // ArmorClassValue on a shield stores the AC adjustment (e.g. -1 = improves AC by 1).
    public bool IsShield { get; set; } = false;

    // Weapon metadata for combat tracking.
    public bool IsWeapon { get; set; } = false;
    public int WeaponSpeed { get; set; } = 0;
    public string WeaponDamageSmallMedium { get; set; } = "";
    public string WeaponDamageLarge { get; set; } = "";
    public string WeaponType { get; set; } = "";
    public string WeaponSize { get; set; } = "";

    // Cost fields are stored by coin type to support mixed-economy tables.
    public int CostCopper { get; set; } = 0;
    public int CostSilver { get; set; } = 0;
    public int CostGold { get; set; } = 0;

    public bool IsContainer { get; set; } = false;
    public int ContainerMaxItems { get; set; } = 0;
    public double ContainerMaxWeight { get; set; } = 0;
    public List<string> AllowedContentTags { get; set; } = new();
}

public record KitDefinition(
    string Id,
    string Name,
    string Description,
    string Source,
    List<string> AllowedRaces,
    List<string> AllowedClasses,
    List<string> FreeNwpIds,      // NWPs granted at no slot/CP cost
    List<string> RequiredNwpIds,  // NWPs the character must take (costs normally)
    string RulesMode = "all"     // all | core_rules | players_option
);

public record MultiClassComboGroup(
    string RaceGroup,              // e.g. "elf", "half-elf", "dwarf"
    string Source,                 // e.g. "PHB 2e"
    List<List<string>> Combos      // each inner list is a valid set of class IDs
);

/// <summary>One age stage for a dragon (AD&amp;D 2e has 12 categories: Wyrmling → Great Wyrm).</summary>
public record DragonAgeStage(
    int    StageNumber,       // 1–12
    string Category,          // "Wyrmling", "Very Young", ..., "Great Wyrm"
    string HitDice,           // e.g. "9" or "9+2"
    int    ArmorClass,
    int    Thac0,
    string BreathWeapon,      // dice notation e.g. "2d10+9"
    string SpellLevel,        // highest castable e.g. "1st", "None"
    string MagicResistance,   // e.g. "20%", "Nil"
    string SpecialAbilities   // additional abilities unlocked at this age
);

public record MonsterDefinition(
    string Id,
    string Name,
    string MonsterType,
    string Source,
    string HitDice,
    int ArmorClass,
    string Movement,
    int Thac0,
    int Attacks,
    string Damage,
    string SpecialAttacks,
    string SpecialDefenses,
    string MagicResistance,
    string Size,           // T/S/M/L/H/G
    string Morale,
    int XpValue,
    string NumberAppearing,
    string Frequency,
    string Intelligence,
    string Alignment,
    string TreasureType,
    string Description,
    List<DragonAgeStage>? AgeStages = null,   // null for non-dragons
    string SpecialAbilities = "",            // broad abilities not covered by attacks/defenses
    string Combat = "",                      // Combat section text
    string HabitatSociety = "",              // Habitat/Society section text
    string Ecology = ""                      // Ecology section text
)
{
    // Preserves source text for non-numeric THAC0 values like "Varies" or "N/A".
    public string Thac0Text { get; init; } = "";

    public string EffectiveThac0Text => string.IsNullOrWhiteSpace(Thac0Text) ? Thac0.ToString() : Thac0Text;

    public int? HitDiceMin => ParseHitDiceRange(HitDice).min;
    public int? HitDiceMax => ParseHitDiceRange(HitDice).max;
    public int? Thac0Min => ParseIntRange(EffectiveThac0Text).min;
    public int? Thac0Max => ParseIntRange(EffectiveThac0Text).max;

    private static (int? min, int? max) ParseHitDiceRange(string value)
    {
        var s = (value ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) return (null, null);
        if (s is "varies" or "variable" or "n/a" or "nil") return (null, null);

        s = s.Replace("hp", "").Trim();

        if (s is "1/2" or "½") return (0, 1);

        var plusMatch = System.Text.RegularExpressions.Regex.Match(s, "^(\\d+)\\s*\\+\\s*(\\d+)$");
        if (plusMatch.Success)
        {
            int baseHd = int.Parse(plusMatch.Groups[1].Value);
            int bonus = int.Parse(plusMatch.Groups[2].Value);
            return (baseHd, baseHd + bonus);
        }

        var rangeMatch = System.Text.RegularExpressions.Regex.Match(s, "^(\\d+)\\s*[-to]+\\s*(\\d+)$");
        if (rangeMatch.Success)
        {
            int a = int.Parse(rangeMatch.Groups[1].Value);
            int b = int.Parse(rangeMatch.Groups[2].Value);
            return (System.Math.Min(a, b), System.Math.Max(a, b));
        }

        if (int.TryParse(s, out int exact)) return (exact, exact);

        return (null, null);
    }

    private static (int? min, int? max) ParseIntRange(string value)
    {
        var s = (value ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) return (null, null);

        if (int.TryParse(s, out int exact)) return (exact, exact);

        var rangeMatch = System.Text.RegularExpressions.Regex.Match(s, "^(-?\\d+)\\s*[-to]+\\s*(-?\\d+)$");
        if (rangeMatch.Success)
        {
            int a = int.Parse(rangeMatch.Groups[1].Value);
            int b = int.Parse(rangeMatch.Groups[2].Value);
            return (System.Math.Min(a, b), System.Math.Max(a, b));
        }

        return (null, null);
    }
}

public record SpellDefinition(
    string Id,
    string Category,
    string Level,
    string Name,
    string Reversal,
    string Schools,
    string Range,
    string Components,
    string Materials,
    string CastTime,
    string Duration,
    string Area,
    string Save,
    string Frequency,
    string Volume,
    string Page,
    bool IsHealing,
    string Damage,
    string DamageStep,
    string DamageScaleStartLevel,
    string DamageScaleEveryLevels,
    string DamageMaxAtLevel,
    string DamageMax,
    string BriefDescription,
    string Description
);

public record TraitDefinition(
    string Id,
    string Name,
    int Cost,
    string Description
);

public record DisadvantageDefinition(
    string Id,
    string Name,
    int ModerateBonus,
    int? SevereBonus,
    string Description
);

public record CharacterOptionCatalog(
    IReadOnlyList<NonweaponProficiencyDefinition> NonweaponProficiencies,
    IReadOnlyList<TraitDefinition> Traits,
    IReadOnlyList<DisadvantageDefinition> Disadvantages
);

// ── Character sheet ───────────────────────────────────────────────────────────

public class CharacterSheet
{
    public string Name          { get; set; } = "";
    public string PlayerName    { get; set; } = "";
    public string Party         { get; set; } = "";
    public string RaceId        { get; set; } = "";
    public string ClassId       { get; set; } = "";
    public string CharacterMode { get; set; } = "core_rules";
    public int    Level         { get; set; } = 1;
    public int    ExperiencePoints { get; set; } = 0;

    // Character currency tracked in coin denominations.
    public int GoldPieces { get; set; } = 0;
    public int SilverPieces { get; set; } = 0;
    public int CopperPieces { get; set; } = 0;
    public bool StartingFundsAssigned { get; set; } = false;

    // Base HP (hit die + CON mod) before bonuses
    public int    BaseHitPoints { get; set; } = 8;

    // Effective HP after all flat bonuses are applied
    public int    HitPoints     { get; set; } = 8;

    // Current HP from last combat (tracks damage between encounters)
    public int    CurrentHitPoints { get; set; } = 8;

    // Base AC (10 = unarmoured) before bonuses
    public int    BaseArmorClass  { get; set; } = 10;

    // Effective AC after all bonuses are applied
    public int    ArmorClass      { get; set; } = 10;

    // Armor profile used for conditional mechanics (e.g., no-armor AC bonuses).
    public string ArmorProfile    { get; set; } = "no_armor";

    // Effective THAC0 after class progression and attack bonuses
    public int    Thac0           { get; set; } = 20;

    // Warrior melee progression from S&P Table 18.
    public string AttackRate       { get; set; } = "1/round";

    // Base movement rate before armor/encumbrance modifiers
    public int    BaseMovement    { get; set; } = 12;

    // Effective movement rate from abilities before armor/encumbrance penalties
    public int    Movement        { get; set; } = 12;

    public int    Revision       { get; set; } = 1;
    public DateTime LastModified { get; set; } = DateTime.Now;

    // Level-up rewards that can be spent later.
    public int UnspentProficiencyChoices { get; set; } = 0;
    public int UnspentRogueSkillPoints { get; set; } = 0;
    public int UnspentCharacterPoints { get; set; } = 0;
    public int SpentNwpCharacterPoints { get; set; } = 0;
    public int SpentWeaponCharacterPoints { get; set; } = 0;

    // HP gained when reaching each level (key = achieved level, value = HP gained at that level).
    public Dictionary<int, int> HitPointGainByLevel { get; set; } = new();

    // Spell slots by spell level (key: spell level, value: slots).
    public Dictionary<int, int> DivineSpellSlots { get; set; } = new();
    public Dictionary<int, int> ArcaneSpellSlots { get; set; } = new();
    public List<string> WizardSpellbookIds { get; set; } = new();
    public List<WizardSpellbook> WizardSpellbooks { get; set; } = new();
    public List<NamedSpellList> WizardSpellLists { get; set; } = new();
    public List<string> TrackedSpellIds { get; set; } = new();

    public Dictionary<string, int> Abilities       { get; set; } = new();
    public List<string>            Notes           { get; set; } = new();

    // Legacy flat-text list (populated for display)
    public List<string>            RacialAbilities { get; set; } = new();

    // Structured ability definitions (racial + class combined)
    public List<AbilityDefinition> StructuredAbilities { get; set; } = new();

    // Rolled-up mechanical bonuses
    public AbilityBonuses          Bonuses         { get; set; } = new();

    // Racial point-buy accounting (Player's Option races)
    public int                     RacialPointBudget { get; set; } = 0;
    public int                     RacialPointSpent  { get; set; } = 0;
    public int                     RacialPointRemaining { get; set; } = 0;
    public int                     ClassPointBudget { get; set; } = 0;
    public int                     ClassPointSpent { get; set; } = 0;
    public int                     ClassPointRemaining { get; set; } = 0;
    public int                     ClassAbilityCarryoverPoints { get; set; } = 0;
    public List<string>            SelectedRacialAbilityIds { get; set; } = new();
    public List<string>            SelectedClassAbilityIds { get; set; } = new();
    public string                  WizardSpecializationId  { get; set; } = "";
    public List<string>            NonweaponProficiencies { get; set; } = new();
    public List<string>            Languages { get; set; } = new();
    public List<string>            Equipment { get; set; } = new();
    public List<string>            Traits { get; set; } = new();
    public List<string>            Disadvantages { get; set; } = new();
    // PO sub-abilities: keys e.g. "str_muscle", "str_stamina", "dex_aim" …
    public Dictionary<string, int> SubAbilities { get; set; } = new();
    // Exceptional strength percentile (warriors with STR 18): 0=none, 1-100
    public int ExceptionalStrength { get; set; } = 0;
    // Derived mechanical totals from sub-abilities (encumbrance, initiative, social, etc.)
    public Dictionary<string, int> DerivedStats { get; set; } = new();
    // Per-sub-ability display snapshots at build time (key => rendered effect text)
    public Dictionary<string, string> SubAbilityEffects { get; set; } = new();

    public string RaceName  { get; set; } = "";
    public string ClassName { get; set; } = "";

    // Stored chargen identity for level-up/edit flows.
    public string ClassMode { get; set; } = "";
    public List<string> ClassIds { get; set; } = new();

    // Structured selected options for later editing/level-up.
    public List<string> NonweaponProficiencyIds { get; set; } = new();
    public List<WeaponProficiencySelection> WeaponProficiencies { get; set; } = new();
    public List<EquipmentSelection> EquipmentSelections { get; set; } = new();

    // Selections added in the most recent level-up; locked during the next level-up cycle.
    public List<string> LockedLastLevelUpNonweaponIds { get; set; } = new();
    public List<string> LockedLastLevelUpWeaponIds { get; set; } = new();

    // CP automatically awarded per level-up (0 = prompt each time).
    public int CpPerLevel { get; set; } = 0;

    // Full chargen selection state retained for level-up re-entry.
    public Dictionary<string, string>  SelectedSpheres { get; set; } = new();
    public Dictionary<string, bool>    SelectedWizardSchools { get; set; } = new();
    public Dictionary<string, List<string>> SelectedAbilitiesByClass { get; set; } = new();
    public List<string>                SelectedTraitIds { get; set; } = new();
    public Dictionary<string, string>  SelectedDisadvantageSeverities { get; set; } = new();
    public List<LanguageSelection>     SelectedLanguages { get; set; } = new();
    public Dictionary<string, int>     SelectedNonweaponProficiencyImprovements { get; set; } = new();
    public string                      RogueSkillArmorProfile { get; set; } = "no_armor";
    public Dictionary<string, int>     SelectedRogueSkillPoints { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> SpheresByClass { get; set; } = new();
    public Dictionary<string, Dictionary<string, bool>>   SchoolsByClass { get; set; } = new();
    // Explicitly equipped slots — empty string means "auto-select from inventory".
    public string EquippedArmorId  { get; set; } = "";
    public string EquippedShieldId { get; set; } = "";
    public string EquippedWeaponId { get; set; } = "";
    // Kit selection (from Complete Books of Races)
    public string KitId { get; set; } = "";
    public string KitName { get; set; } = "";
    public List<string> KitFreeNwpIds { get; set; } = new();
    public List<string> KitRequiredNwpIds { get; set; } = new();
    
    // ── Spell Tracking System ──────────────────────────────────────────
    // Full spell tracking for this character (daily casting, preparation, history)
    public CharacterSpellTracking? SpellTracking { get; set; }

    // Priest memorization bypass flag (if true, priests cast without daily prep)
    public bool PriestMemorizationBypass { get; set; } = false;

    // Linked campaign for spell tracking (to sync with DM's calendar)
    public string LinkedCampaignIdForSpells { get; set; } = "";

    public string LastModifiedDisplay => LastModified.ToString("yyyy-MM-dd");
}

public class LanguageSelection
{
    public string SourceKey { get; set; } = "free_spoken";
    public string LanguageName { get; set; } = "";
}

public class NamedSpellList
{
    public string Name { get; set; } = "";
    public List<string> SpellIds { get; set; } = new();
}

public class WizardSpellbook
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Spellbook";
    public string Type { get; set; } = "Standard"; // Traveling, Standard, Tome
        /// <summary>ItemId of the EquipmentSelection in the character's inventory that this book corresponds to.
        /// Empty if the book was created without a matching inventory item.</summary>
        public string InventoryItemId { get; set; } = "";
    public int CapacityPages { get; set; } = 100;
    public double WeightLbs { get; set; } = 15;
    public string Dimensions { get; set; } = "16\" x 12\" x 6\""; // Width x Height x Depth
    public Dictionary<string, int> SpellPages { get; set; } = new(); // Maps spell ID to pages used
    
    /// <summary>
    /// Gets the total pages used by all spells in this spellbook.
    /// </summary>
    public int GetTotalPagesUsed()
    {
        return SpellPages.Values.Sum();
    }
    
    /// <summary>
    /// Gets the number of pages available in this spellbook.
    /// </summary>
    public int GetAvailablePages()
    {
        return Math.Max(0, CapacityPages - GetTotalPagesUsed());
    }
}

// ── Weapon Proficiency System ────────────────────────────────────────────────

/// <summary>
/// A single weapon entry with full AD&D 2e combat statistics.
/// </summary>
public class WeaponDefinition
{
    public string Id             { get; set; } = "";
    public string Name           { get; set; } = "";
    public string GroupId        { get; set; } = "";    // broad group (e.g. "axes")
    public string TightGroupId   { get; set; } = "";    // tight group (e.g. "light_axes")
    public string DamageSm       { get; set; } = "1d6"; // damage vs. Small/Medium
    public string DamageL        { get; set; } = "1d6"; // damage vs. Large
    public int    Speed          { get; set; } = 5;     // weapon speed factor (actual number)
    public string Type           { get; set; } = "B";   // S=Slashing, P=Piercing, B=Bludgeoning
    public string Size           { get; set; } = "M";   // S/M/L
    public string AttacksPerRound { get; set; } = "1";  // "1", "1/2", "2", "3", etc.
    public string Notes          { get; set; } = "";
}

/// <summary>
/// A tight weapon group — a small cluster of closely related weapons.
/// Proficiency costs 1 slot and covers all weapons in the group.
/// </summary>
public class TightGroupDefinition
{
    public string       Id        { get; set; } = "";
    public string       Name      { get; set; } = "";
    public List<string> WeaponIds { get; set; } = new();
}

/// <summary>
/// A broad weapon group containing tight sub-groups and individual weapons.
/// Proficiency costs 2 slots and covers the entire family.
/// </summary>
public class WeaponGroupDefinition
{
    public string                    Id          { get; set; } = "";
    public string                    Name        { get; set; } = "";
    public string                    Description { get; set; } = "";
    public List<TightGroupDefinition> TightGroups { get; set; } = new();
}

/// <summary>
/// Records a single weapon proficiency selection made during character creation.
/// </summary>
public class WeaponProficiencySelection
{
    /// <summary>
    /// The weapon/tight-group/broad-group id that is selected.
    /// </summary>
    public string ProficiencyId   { get; set; } = "";
    /// <summary>"individual", "tight_group", or "broad_group"</summary>
    public string ProficiencyType { get; set; } = "individual";
    /// <summary>
    /// Display name stored so the review screen can show it without needing the full weapon catalog.
    /// </summary>
    public string DisplayName     { get; set; } = "";
    /// <summary>
    /// Whether the character is specialized in this weapon (warriors only; costs 1 extra slot).
    /// </summary>
    public bool   Specialized     { get; set; } = false;
    /// <summary>
    /// Whether this individual weapon is marked as Weapon of Choice.
    /// </summary>
    public bool   WeaponOfChoice  { get; set; } = false;
    /// <summary>
    /// Whether this individual weapon is marked as Weapon Expertise.
    /// </summary>
    public bool   WeaponExpertise { get; set; } = false;
}

public class EquipmentSelection
{
    // Stable catalog entry id, currently "<tableCode>|<itemName>".
    public string ItemId { get; set; } = "";
    public string Category { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string CostText { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public bool IsArmor { get; set; } = false;
    public int ArmorClassValue { get; set; } = 10;
    public string RogueArmorProfile { get; set; } = "no_armor";
    public bool IsShield { get; set; } = false;
    public bool IsWeapon { get; set; } = false;
    public int WeaponSpeed { get; set; } = 0;
    public string WeaponDamageSmallMedium { get; set; } = "";
    public string WeaponDamageLarge { get; set; } = "";
    public string WeaponType { get; set; } = "";
    public string WeaponSize { get; set; } = "";
    public int CostGoldEach { get; set; } = 0;
    public int CostSilverEach { get; set; } = 0;
    public int CostCopperEach { get; set; } = 0;
    public string SizeClassEach { get; set; } = "Medium";
    public double WeightEach { get; set; } = 0;
}

public class Combatant
{
    public string CombatantId { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "PC";
    public string SourceName { get; set; } = "";
    public string PartyName { get; set; } = "";
    public string MonsterBaseName { get; set; } = "";
    public int MonsterNumber { get; set; } = 0;
    public int ArmorClass { get; set; }
    public int Thac0 { get; set; }
    public string Thac0Text { get; set; } = "";
    public string Name       { get; set; } = "";
    public int    Initiative { get; set; }
    public int    HpCurrent  { get; set; }
    public int    HpMax      { get; set; }
    public string AttackPatternText { get; set; } = "1";
    public string DamageProfile { get; set; } = "";
    public string AssignedTargetId { get; set; } = "";
    public string AssignedTargetName { get; set; } = "";
    public int AttackCursor { get; set; } = 0;
    public int AttacksRemainingThisRound { get; set; } = 1;
    public List<string> Statuses { get; set; } = new();
    public bool IsActiveTurn { get; set; } = false;
    public int SpeedFactor { get; set; } = 0;
    public string DamageRollExpression { get; set; } = "";

    public bool IsMonster => string.Equals(Kind, "Monster", StringComparison.OrdinalIgnoreCase);
    public bool IsPc => !IsMonster;
    public bool IsPlayerCharacter => IsPc;
    public bool IsCharmed { get; set; } = false;
    public bool IsDead => HpCurrent <= -10;
    public bool IsUnconscious => !IsDead && HpCurrent <= 0;
    public bool IsAbleToAct => HpCurrent > 0;
    public string ConditionDisplay => IsDead ? "Dead" : (IsUnconscious ? "Unconscious" : string.Empty);
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (SourceName ?? string.Empty) : Name;
    public string DisplayTag
    {
        get
        {
            string baseTag = string.IsNullOrWhiteSpace(PartyName) ? Kind : $"{Kind} · {PartyName}";
            string conditionTag = string.IsNullOrWhiteSpace(ConditionDisplay) ? baseTag : $"{baseTag} · {ConditionDisplay}";
            var activeEffects = Statuses
                .Where(s => !string.IsNullOrWhiteSpace(s)
                    && !string.Equals(s, "Unconscious", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(s, "Dead", StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            return activeEffects.Count == 0 ? conditionTag : $"{conditionTag} · {string.Join(", ", activeEffects)}";
        }
    }
    public string Thac0Display => string.IsNullOrWhiteSpace(Thac0Text) ? Thac0.ToString() : Thac0Text;
    public string AttackDisplay => string.IsNullOrWhiteSpace(AttackPatternText) ? "1" : AttackPatternText;
    public string DamageDisplay => string.IsNullOrWhiteSpace(DamageProfile) ? "—" : DamageProfile;
    public string TargetDisplay => string.IsNullOrWhiteSpace(AssignedTargetName) ? "No target" : $"Target: {AssignedTargetName}";

    public string Display =>
        $"  {Initiative,3}  {DisplayName,-24}  {DisplayTag,-14}  HP {HpCurrent}/{HpMax}  AC {ArmorClass,2}  THAC0 {Thac0,2}{(IsDead ? "  [DEAD]" : (IsUnconscious ? "  [UNCONSCIOUS]" : string.Empty))}";
}

public class Encounter
{
    public string          Name        { get; set; } = "";
    public int             RoundNumber { get; set; } = 1;
    public List<Combatant> Combatants  { get; set; } = new();
    public List<CombatLogEntry> CombatLog { get; set; } = new();
    public List<GameEventEntry> Timeline { get; set; } = new();
}

public enum GameEventCategory
{
    Combat,
    Treasure,
    Campaign,
    Narrative,
    System,
    Custom
}

public class GameEventEntry
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public int RoundNumber { get; set; } = 0;
    public GameEventCategory Category { get; set; } = GameEventCategory.Custom;
    public string EventType { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string TargetName { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Details { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

// ── Combat Logging ────────────────────────────────────────────────────────────

public enum CombatActionType
{
    MonsterAttack,
    PlayerAttack,
    SpellCast,
    SpellEffect,
    Damage,
    StateChange,    // Death, Unconscious, etc.
    Initiative,
    RoundStart,
    RoundEnd
}

public class CombatLogEntry
{
    public int                Round           { get; set; }
    public CombatActionType   ActionType      { get; set; }
    public string             ActorName       { get; set; } = "";
    public string             TargetName      { get; set; } = "";
    public int                RollValue       { get; set; } = -1;   // For attack rolls, save rolls, etc.
    public int                TargetValue     { get; set; } = -1;   // For AC, Save DC, etc.
    public bool               Success         { get; set; }         // Hit/Miss, Save Success/Fail, etc.
    public int                Damage          { get; set; } = 0;
    public string             Details         { get; set; } = "";    // Spell name, effect name, condition, etc.
    public int                HpBefore        { get; set; } = -1;
    public int                HpAfter         { get; set; } = -1;
    public DateTime           Timestamp       { get; set; } = DateTime.Now;

    public override string ToString()
    {
        return ActionType switch
        {
            CombatActionType.MonsterAttack => $"R{Round}: {ActorName} rolled {RollValue} vs AC {TargetValue} — {(Success ? "HIT" : "MISS")} on {TargetName}",
            CombatActionType.PlayerAttack => $"R{Round}: {ActorName} rolled {RollValue} vs AC {TargetValue} — {(Success ? "HIT" : "MISS")} on {TargetName}",
            CombatActionType.SpellCast => $"R{Round}: {ActorName} cast {Details} on {TargetName}",
            CombatActionType.SpellEffect => $"R{Round}: {Details} effect applied to {TargetName}",
            CombatActionType.Damage => $"R{Round}: {TargetName} took {Damage} damage (HP {HpBefore}→{HpAfter}){(string.IsNullOrWhiteSpace(Details) ? "" : $" — {Details}")}",
            CombatActionType.StateChange => $"R{Round}: {TargetName} is now {Details}",
            CombatActionType.Initiative => $"Initiative Rolled: {Details}",
            CombatActionType.RoundStart => $"Round {Round} started",
            CombatActionType.RoundEnd => $"Round {Round} ended",
            _ => $"R{Round}: {ActionType} — {Details}"
        };
    }
}

// ── Treasure Tables (DMG) ────────────────────────────────────────────────────

public record DmgTreasureRoll
{
    public string Copper { get; init; } = "";      // cp
    public string CopperChance { get; init; } = "";
    public string Silver { get; init; } = "";      // sp
    public string SilverChance { get; init; } = "";
    public string Electrum { get; init; } = "";    // ep
    public string ElectrumChance { get; init; } = "";
    public string Gold { get; init; } = "";        // gp
    public string GoldChance { get; init; } = "";
    public string Platinum { get; init; } = "";    // pp
    public string PlatinumChance { get; init; } = "";
    public string Gems { get; init; } = "";
    public string GemsChance { get; init; } = "";
    public string Jewelry { get; init; } = "";
    public string JewelryChance { get; init; } = "";
    public string MagicItems { get; init; } = "";
    public string MagicItemsChance { get; init; } = "";
}

public class DmgTreasureTable
{
    // Treasure Type: A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z
    public static readonly Dictionary<string, DmgTreasureRoll> Tables = new()
    {
        // Standard DMG treasure types (simplified for the tracker)
        { "A", new() { Copper = "1,000-3,000", CopperChance = "25%", Silver = "200-2,000", SilverChance = "30%", Gold = "1,000-6,000", GoldChance = "40%", Platinum = "300-1,800", PlatinumChance = "35%", Gems = "10-40", GemsChance = "60%", Jewelry = "2-12", JewelryChance = "50%", MagicItems = "Any 3", MagicItemsChance = "30%" } },
        { "B", new() { Copper = "1,000-6,000", CopperChance = "50%", Silver = "1,000-3,000", SilverChance = "25%", Gold = "200-2,000", GoldChance = "25%", Platinum = "100-1,000", PlatinumChance = "25%", Gems = "1-8", GemsChance = "30%", Jewelry = "1-4", JewelryChance = "20%", MagicItems = "Armor Weapon", MagicItemsChance = "10%" } },
        { "C", new() { Copper = "1,000-10,000", CopperChance = "20%", Silver = "1,000-6,000", SilverChance = "30%", Platinum = "100-600", PlatinumChance = "10%", Gems = "1-6", GemsChance = "25%", Jewelry = "1-3", JewelryChance = "20%", MagicItems = "Any 2", MagicItemsChance = "10%" } },
        { "D", new() { Copper = "1,000-6,000", CopperChance = "10%", Silver = "1,000-10,000", SilverChance = "15%", Gold = "1,000-3,000", GoldChance = "50%", Platinum = "100-600", PlatinumChance = "15%", Gems = "1-10", GemsChance = "30%", Jewelry = "1-6", JewelryChance = "25%", MagicItems = "Any 2 + 1 potion", MagicItemsChance = "15%" } },
        { "E", new() { Copper = "1,000-6,000", CopperChance = "5%", Silver = "1,000-10,000", SilverChance = "25%", Gold = "1,000-4,000", GoldChance = "25%", Platinum = "300-1,800", PlatinumChance = "25%", Gems = "1-12", GemsChance = "15%", Jewelry = "1-6", JewelryChance = "10%", MagicItems = "Any 3 + 1 scroll", MagicItemsChance = "25%" } },
        { "F", new() { Silver = "3,000-18,000", SilverChance = "10%", Gold = "1,000-6,000", GoldChance = "40%", Platinum = "1,000-4,000", PlatinumChance = "15%", Gems = "2-20", GemsChance = "20%", Jewelry = "1-8", JewelryChance = "10%", MagicItems = "Any 5 except weapons", MagicItemsChance = "30%" } },
        { "G", new() { Gold = "2,000-20,000", GoldChance = "50%", Platinum = "1,000-10,000", PlatinumChance = "50%", Gems = "3-18", GemsChance = "30%", Jewelry = "1-6", JewelryChance = "25%", MagicItems = "Any 5", MagicItemsChance = "35%" } },
        { "H", new() { Copper = "3,000-18,000", CopperChance = "25%", Silver = "2,000-20,000", SilverChance = "40%", Gold = "2,000-20,000", GoldChance = "55%", Platinum = "1,000-8,000", PlatinumChance = "40%", Gems = "3-30", GemsChance = "50%", Jewelry = "2-20", JewelryChance = "50%", MagicItems = "Any 6", MagicItemsChance = "15%" } },
        { "I", new() { Platinum = "100-600", PlatinumChance = "30%", Gems = "2-12", GemsChance = "55%", Jewelry = "2-8", JewelryChance = "50%", MagicItems = "Any 1", MagicItemsChance = "15%" } },
        { "J", new() { Copper = "3-24" } },
        { "K", new() { Silver = "3-18" } },
        { "L", new() { Platinum = "2-12" } },
        { "M", new() { Gold = "2-8" } },
        { "N", new() { Platinum = "1-6" } },
        { "O", new() { Copper = "10-40", Silver = "10-30" } },
        { "P", new() { Silver = "10-60", Platinum = "1-20" } },
        { "Q", new() { Gems = "1-4" } },
        { "R", new() { Gold = "2-20", Platinum = "10-60", Gems = "2-8", Jewelry = "1-3" } },
        { "S", new() { MagicItems = "1-8 potions" } },
        { "T", new() { MagicItems = "1-4 scrolls" } },
        { "U", new() { Gems = "2-16", GemsChance = "90%", Jewelry = "1-6", JewelryChance = "80%", MagicItems = "Any 1", MagicItemsChance = "70%" } },
        { "V", new() { MagicItems = "Any 2" } },
        { "W", new() { Gold = "5-30", Platinum = "1-8", Gems = "2-16", GemsChance = "60%", Jewelry = "1-8", JewelryChance = "50%", MagicItems = "Any 2", MagicItemsChance = "60%" } },
        { "X", new() { MagicItems = "Any 2 potions" } },
        { "Y", new() { Gold = "200-1,200" } },
        { "Z", new() { Copper = "100-300", Silver = "100-400", Gold = "100-600", Platinum = "100-400", Gems = "1-6", GemsChance = "55%", Jewelry = "2-12", JewelryChance = "50%", MagicItems = "Any 3", MagicItemsChance = "50%" } },
    };

    public static string GetTreasureTable(string treasureType)
    {
        var typeUppercase = (treasureType ?? "").Trim().ToUpperInvariant();
        if (Tables.TryGetValue(typeUppercase, out var roll))
        {
            var parts = new List<string>();
            static string FormatWithChance(string label, string value, string chance)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return string.Empty;
                string chanceText = string.IsNullOrWhiteSpace(chance) ? "auto" : chance;
                return $"{label} {value} ({chanceText})";
            }

            void AddPart(string label, string value, string chance)
            {
                var text = FormatWithChance(label, value, chance);
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }

            AddPart("CP", roll.Copper, roll.CopperChance);
            AddPart("SP", roll.Silver, roll.SilverChance);
            AddPart("EP", roll.Electrum, roll.ElectrumChance);
            AddPart("GP", roll.Gold, roll.GoldChance);
            AddPart("PP", roll.Platinum, roll.PlatinumChance);
            AddPart("Gems", roll.Gems, roll.GemsChance);
            AddPart("Art", roll.Jewelry, roll.JewelryChance);
            AddPart("Magic", roll.MagicItems, roll.MagicItemsChance);
            return string.Join(" | ", parts);
        }
        return "Unknown treasure type";
    }

    public static Dictionary<string, DmgTreasureRoll> GetAllTables()
    {
        return Tables
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryUpdateTreasureTable(string treasureType, DmgTreasureRoll roll)
    {
        string typeUppercase = (treasureType ?? string.Empty).Trim().ToUpperInvariant();
        if (typeUppercase.Length != 1 || typeUppercase[0] < 'A' || typeUppercase[0] > 'Z')
            return false;

        Tables[typeUppercase] = roll with
        {
            Copper = (roll.Copper ?? string.Empty).Trim(),
            CopperChance = (roll.CopperChance ?? string.Empty).Trim(),
            Silver = (roll.Silver ?? string.Empty).Trim(),
            SilverChance = (roll.SilverChance ?? string.Empty).Trim(),
            Electrum = (roll.Electrum ?? string.Empty).Trim(),
            ElectrumChance = (roll.ElectrumChance ?? string.Empty).Trim(),
            Gold = (roll.Gold ?? string.Empty).Trim(),
            GoldChance = (roll.GoldChance ?? string.Empty).Trim(),
            Platinum = (roll.Platinum ?? string.Empty).Trim(),
            PlatinumChance = (roll.PlatinumChance ?? string.Empty).Trim(),
            Gems = (roll.Gems ?? string.Empty).Trim(),
            GemsChance = (roll.GemsChance ?? string.Empty).Trim(),
            Jewelry = (roll.Jewelry ?? string.Empty).Trim(),
            JewelryChance = (roll.JewelryChance ?? string.Empty).Trim(),
            MagicItems = (roll.MagicItems ?? string.Empty).Trim(),
            MagicItemsChance = (roll.MagicItemsChance ?? string.Empty).Trim(),
        };

        return true;
    }
}

public class PartyLootEntry
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string Source { get; set; } = ""; // e.g., "Treasure Type G, Session 12"
    public string Notes { get; set; } = "";
    public string ListDisplay =>
        string.IsNullOrWhiteSpace(SessionId)
            ? (Quantity > 1 ? $"{Name} x{Quantity}" : Name)
            : (Quantity > 1 ? $"{Name} x{Quantity}  [S:{SessionId}]" : $"{Name}  [S:{SessionId}]");
}

public class CampaignEntry
{
    public string       SessionId { get; set; } = "";
    public string       Title     { get; set; } = "";
    public string       Body      { get; set; } = "";
    public string       WorldDate { get; set; } = "";
    public List<string> Tags      { get; set; } = new();

    public string ListDisplay => string.IsNullOrWhiteSpace(WorldDate)
        ? $"[{SessionId}]  {Title}"
        : $"[{SessionId}]  {Title}  ({WorldDate})";
}

public class NpcEntry
{
    public string Name  { get; set; } = "";
    public string Role  { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ListDisplay => string.IsNullOrEmpty(Role) ? Name : $"{Name}  ({Role})";
}

public class LocationEntry
{
    public string Name  { get; set; } = "";
    public string Type  { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ListDisplay => string.IsNullOrEmpty(Type) ? Name : $"{Name}  [{Type}]";
}

// ── Calendar System ──────────────────────────────────────────────────────────

public class MoonDefinition
{
    public string Name { get; set; } = "";
    public int CycleLengthDays { get; set; } = 29;  // ~lunar month
    public int DayInCycle { get; set; } = 0;        // 0 = new moon, progression through cycle

    // Calculate moon phase (0-7 for eight phases)
    public int GetPhase() => (DayInCycle * 8) / CycleLengthDays;

    public string GetPhaseText()
    {
        return GetPhase() switch
        {
            0 => "New Moon",
            1 => "Waxing Crescent",
            2 => "First Quarter",
            3 => "Waxing Gibbous",
            4 => "Full Moon",
            5 => "Waning Gibbous",
            6 => "Last Quarter",
            7 => "Waning Crescent",
            _ => "Unknown"
        };
    }

    public string Illumination
    {
        get
        {
            double phase = GetPhase();
            double percent = phase < 4
                ? (phase / 4.0) * 100
                : ((8 - phase) / 4.0) * 100;
            return $"{percent:F0}%";
        }
    }
}

public class CalendarConfiguration
{
    public int MonthCount { get; set; } = 12;
    public int DaysPerWeek { get; set; } = 7;
    public int HoursPerDay { get; set; } = 24;
    public int DayOfWeekOffset { get; set; } = 0;  // Offset to align calendar days of week (0 = day 1 at column 0)
    public List<string> MonthNames { get; set; } = new()
    {
        "Ja", "Fe", "Ma", "Ap", "Ma", "Ju",
        "Ju", "Au", "Se", "Oc", "No", "De"
    };
    public List<string> DayNames { get; set; } = new()
    {
        "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"
    };
    // Per-month day counts. If empty or shorter than MonthCount, falls back to alternating 30/31.
    public List<int> DaysPerMonth { get; set; } = new();
    public List<MoonDefinition> Moons { get; set; } = new();

    // Returns days in a given month
    public int GetDaysInMonth(int monthIndex)
    {
        if (monthIndex < 0 || monthIndex >= MonthCount) return 0;
        if (DaysPerMonth.Count > monthIndex && DaysPerMonth[monthIndex] > 0)
            return DaysPerMonth[monthIndex];
        // Fallback: alternate 30/31 days
        return monthIndex % 2 == 0 ? 31 : 30;
    }

    // Ensure DaysPerMonth list matches MonthCount, filling gaps with alternating 30/31
    public void NormalizeDaysPerMonth()
    {
        while (DaysPerMonth.Count < MonthCount)
        {
            int idx = DaysPerMonth.Count;
            DaysPerMonth.Add(idx % 2 == 0 ? 31 : 30);
        }
        if (DaysPerMonth.Count > MonthCount)
            DaysPerMonth.RemoveRange(MonthCount, DaysPerMonth.Count - MonthCount);
    }
}

public class CalendarEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = "";  // e.g., "Combat", "Treasure", "Note"
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string RelatedId { get; set; } = "";  // e.g., EncounterId, TreasureRollId
}

public class CalendarDate
{
    public int Year { get; set; } = 0;
    public string Era { get; set; } = "PC";
    public int Month { get; set; } = 0;      // 0-indexed
    public int Day { get; set; } = 1;        // 1-indexed
    public List<CalendarEvent> Events { get; set; } = new();

    public string Display => $"{Day}";
    public bool HasEvents => Events.Count > 0;
    public string EventsPreview => Events.Count > 0 ? $"{Events.Count} event(s)" : "";
}

public class Calendar
{
    public string CalendarId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Campaign Calendar";
    public CalendarConfiguration Config { get; set; } = new();

    // Current date and time in the calendar
    public int CurrentYear { get; set; } = 0;
    public string CurrentEra { get; set; } = "PC";
    public int CurrentMonth { get; set; } = 0;
    public int CurrentDay { get; set; } = 1;
    public int CurrentHour { get; set; } = 0;
    public int CurrentMinute { get; set; } = 0;

    // Track total minutes elapsed for precise time tracking
    public long TotalDaysElapsed { get; set; } = 0;
    public long TotalMinutesElapsed { get; set; } = 0;

    // All calendar events indexed by date for quick lookup
    private Dictionary<string, CalendarDate> _dates = new();

    public Calendar()
    {
        InitializeDates();
    }

    public void InitializeDates()
    {
        _dates.Clear();
        for (int m = 0; m < Config.MonthCount; m++)
        {
            int daysInMonth = Config.GetDaysInMonth(m);
            for (int d = 1; d <= daysInMonth; d++)
            {
                string key = GetDateKey(CurrentYear, CurrentEra, m, d);
                _dates[key] = new CalendarDate { Year = CurrentYear, Era = CurrentEra, Month = m, Day = d };
            }
        }
    }

    public string GetDateKey(int year, string era, int month, int day)
        => $"{year:D8}_{(era ?? string.Empty).Trim().ToUpperInvariant()}_{month:D2}_{day:D2}";

    public string GetDateKey(int year, int month, int day) => GetDateKey(year, CurrentEra, month, day);

    public string GetDateKey(int month, int day) => GetDateKey(CurrentYear, CurrentEra, month, day);

    public CalendarDate GetDate(int month, int day)
    {
        return GetDate(CurrentYear, CurrentEra, month, day);
    }

    public CalendarDate GetDate(int year, int month, int day)
    {
        return GetDate(year, CurrentEra, month, day);
    }

    public CalendarDate GetDate(int year, string era, int month, int day)
    {
        string key = GetDateKey(year, era, month, day);
        if (!_dates.TryGetValue(key, out var date))
        {
            date = new CalendarDate { Year = year, Era = era, Month = month, Day = day };
            _dates[key] = date;
        }
        return date;
    }

    public void AddEvent(int month, int day, CalendarEvent evt)
    {
        AddEvent(CurrentYear, CurrentEra, month, day, evt);
    }

    public void AddEvent(int year, int month, int day, CalendarEvent evt)
    {
        AddEvent(year, CurrentEra, month, day, evt);
    }

    public void AddEvent(int year, string era, int month, int day, CalendarEvent evt)
    {
        var date = GetDate(year, era, month, day);
        date.Events.Add(evt);
    }

    public void RemoveEvent(int month, int day, string eventId)
    {
        RemoveEvent(CurrentYear, CurrentEra, month, day, eventId);
    }

    public void RemoveEvent(int year, int month, int day, string eventId)
    {
        RemoveEvent(year, CurrentEra, month, day, eventId);
    }

    public void RemoveEvent(int year, string era, int month, int day, string eventId)
    {
        var date = GetDate(year, era, month, day);
        date.Events.RemoveAll(e => e.EventId == eventId);
    }

    public void AdvanceDay()
    {
        AdvanceTime(Config.HoursPerDay * 60);
    }

    public void AdvanceTime(int minutes)
    {
        if (minutes <= 0) return;
        TotalMinutesElapsed += minutes;

        int minutesInDay = Config.HoursPerDay * 60;

        CurrentMinute += minutes;

        // Roll over minutes -> hours
        if (CurrentMinute >= 60)
        {
            CurrentHour += CurrentMinute / 60;
            CurrentMinute = CurrentMinute % 60;
        }

        // Roll over hours -> days
        while (CurrentHour >= Config.HoursPerDay)
        {
            CurrentHour -= Config.HoursPerDay;
            CurrentDay++;
            TotalDaysElapsed++;
            int daysInMonth = Config.GetDaysInMonth(CurrentMonth);
            if (CurrentDay > daysInMonth)
            {
                CurrentDay = 1;
                CurrentMonth++;
                if (CurrentMonth >= Config.MonthCount)
                {
                    CurrentMonth = 0;
                    CurrentYear++;
                }
            }
        }

        UpdateMoonCycles();
    }

    public void SetCurrentDate(int month, int day)
    {
        if (month >= 0 && month < Config.MonthCount && day > 0 && day <= Config.GetDaysInMonth(month))
        {
            CurrentMonth = month;
            CurrentDay = day;
            UpdateMoonCycles();
        }
    }

    public void UpdateMoonCycles()
    {
        foreach (var moon in Config.Moons)
        {
            moon.DayInCycle = (int)(TotalDaysElapsed % moon.CycleLengthDays);
        }
    }

    public string GetCurrentDateDisplay()
    {
        string monthName = CurrentMonth < Config.MonthNames.Count
            ? Config.MonthNames[CurrentMonth]
            : $"M{CurrentMonth}";
        return $"{monthName} {CurrentDay}, {CurrentYear} {CurrentEra}  {CurrentHour:D2}:{CurrentMinute:D2}";
    }

    public string GetDateDisplay(int month, int day)
    {
        return GetDateDisplay(CurrentYear, CurrentEra, month, day);
    }

    public string GetDateDisplay(int year, int month, int day)
    {
        return GetDateDisplay(year, CurrentEra, month, day);
    }

    public string GetDateDisplay(int year, string era, int month, int day)
    {
        string monthName = month < Config.MonthNames.Count
            ? Config.MonthNames[month]
            : $"M{month}";
        return $"{monthName} {day}, {year} {era}";
    }

    public IEnumerable<CalendarDate> GetAllDatesWithEvents()
    {
        return _dates.Values
            .Where(d => d.Events.Count > 0)
            .OrderBy(d => d.Year)
            .ThenBy(d => d.Month)
            .ThenBy(d => d.Day);
    }
}
