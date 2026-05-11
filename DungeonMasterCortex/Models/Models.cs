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
    int                        RacialPointBudget = 0
);

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
    List<WizardSpecialization>? Specializations = null
);

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
    List<string> RequiredNwpIds   // NWPs the character must take (costs normally)
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
    public int CapacityPages { get; set; } = 100;
    public Dictionary<string, int> SpellPages { get; set; } = new();
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
    public string Name       { get; set; } = "";
    public int    Initiative { get; set; }
    public int    HpCurrent  { get; set; }
    public int    HpMax      { get; set; }
    public List<string> Statuses { get; set; } = new();

    public string Display =>
        $"  {Initiative,3}  {Name,-24}  HP {HpCurrent}/{HpMax}{(HpCurrent <= 0 ? "  [DEAD]" : "")}";
}

public class Encounter
{
    public string          Name        { get; set; } = "";
    public int             RoundNumber { get; set; } = 1;
    public List<Combatant> Combatants  { get; set; } = new();
}

public class CampaignEntry
{
    public string       SessionId { get; set; } = "";
    public string       Title     { get; set; } = "";
    public string       Body      { get; set; } = "";
    public List<string> Tags      { get; set; } = new();

    public string ListDisplay => $"[{SessionId}]  {Title}";
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
