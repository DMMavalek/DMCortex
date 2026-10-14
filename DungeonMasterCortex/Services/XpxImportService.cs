using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

/// <summary>
/// Experimental importer for TSRP-era .xpx character files.
/// Mapping is intentionally conservative: it extracts stable text tokens
/// (name/class/notes) and leaves uncertain numeric fields at defaults.
/// </summary>
public sealed class XpxImportService
{
    public sealed record XpxImportResult(CharacterSheet Character, IReadOnlyList<string> UnresolvedReferences);
    private sealed record TokenSpan(string Token, int Start, int End);
    private sealed record CoreBlockResult(int? ExperiencePoints, int? Level, int? HitPoints, int[]? Abilities);

    public sealed record XpxImportCatalogs(
        IReadOnlyDictionary<string, string>? SpellIdsByName = null,
        IReadOnlyDictionary<string, string>? NwpIdsByName = null,
        IReadOnlyDictionary<string, CustomEquipmentData>? EquipmentByName = null,
        IReadOnlySet<string>? PlayersOptionNwpIds = null,
        IReadOnlyDictionary<string, string>? TraitIdsByName = null,
        IReadOnlyDictionary<string, string>? DisadvantageIdsByName = null,
        IReadOnlyDictionary<string, string>? WeaponIdsByName = null,
        IReadOnlyDictionary<string, string>? RacialAbilityIdsByName = null,
        IReadOnlyDictionary<string, string>? ClassAbilityIdsByName = null);

    private sealed record TripletAbilityBlockResult(
        int[] BaseAbilities,
        Dictionary<string, int> SubAbilities,
        int ExceptionalStrength,
        int Offset);

    private static readonly HashSet<string> NonCharacterMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "CClassesAllowedOb",
        "CClassesAllowedHelperOb",
        "ClassesAllowedOb",
        "ClassesAllowedHelperOb",
    };

    private static readonly HashSet<string> IgnoredDisplayTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "NONE",
        "upper class",
        "middle class",
        "upper middle class",
        "lower middle class",
        "lower class",
        "lawful good",
        "neutral good",
        "chaotic good",
        "lawful neutral",
        "true neutral",
        "chaotic neutral",
        "lawful evil",
        "neutral evil",
        "chaotic evil",
    };

    private static readonly Dictionary<string, string> ClassIdByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fighter"] = "fighter",
        ["paladin"] = "paladin",
        ["ranger"] = "ranger",
        ["cleric"] = "cleric",
        ["druid"] = "druid",
        ["thief"] = "thief",
        ["bard"] = "bard",
        ["wizard"] = "wizard",
        ["mage"] = "wizard",
        ["illusionist"] = "wizard",
        ["psionicist"] = "psionicist",
    };

    private static readonly Dictionary<string, string> RaceIdByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["human"] = "human",
        ["elf"] = "elf",
        ["half-elf"] = "half-elf",
        ["half elf"] = "half-elf",
        ["dwarf"] = "dwarf",
        ["gnome"] = "gnome",
        ["halfling"] = "halfling",
        ["half-orc"] = "half-orc",
        ["half orc"] = "half-orc",
    };

    private static readonly HashSet<string> ShortKeyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "str", "dex", "con", "int", "wis", "cha",
        "hp", "ac", "xp", "pp", "gp", "sp", "cp",
        "lv", "lvl",
    };

    private static readonly HashSet<string> PlayersOptionSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        "weaponspecialization",
        "limitedarmor",
        "meleecombat",
        "lightsleeping",
        "lightsleeper",
        "dangersense",
        "keeneyesight",
        "phobiaenclosedspaces",
        "claustrophobia",
        "disadvantage",
        "disadvantages",
        "characterpoints",
        "playersoption",
        "skillspowers",
    };

    public XpxImportResult ImportCharacter(
        string filePath,
        IReadOnlyCollection<string>? knownTerms = null,
        IReadOnlyDictionary<string, string>? knownClassIdsByName = null,
        IReadOnlyDictionary<string, string>? knownRaceIdsByName = null,
        XpxImportCatalogs? catalogs = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 32)
            throw new InvalidDataException("XPX file is too small to parse.");

        if (!LooksLikeXpx(bytes))
            throw new InvalidDataException("File does not match expected XPX signature.");

        var orderedSpans = ExtractAsciiTokenSpans(bytes, distinct: false)
            .Where(s => !s.Token.StartsWith("TSRP3", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var orderedTokens = orderedSpans.Select(s => s.Token).ToList();
        var tokens = orderedTokens.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        bool hasNonCharacterMarker = tokens.Any(t => NonCharacterMarkers.Contains(t));

        if (tokens.Count == 0)
            throw new InvalidDataException("XPX file did not contain readable character tokens.");

        string? detectedClassToken = FindClassToken(tokens, knownClassIdsByName);
        if (hasNonCharacterMarker && detectedClassToken is null && !tokens.Any(t => ResolveRaceId(t, knownRaceIdsByName) != "human"))
            throw new InvalidDataException("XPX appears to be an object/helper record, not a character file.");

        string classToken = detectedClassToken ?? "Fighter";
        var classIds = ParseClassIds(classToken, knownClassIdsByName);
        string classId = classIds.FirstOrDefault() ?? "fighter";
        string className = BuildClassName(classIds, classToken);

        string raceToken = FindRaceToken(tokens, knownRaceIdsByName) ?? "Human";
        string raceId = ResolveRaceId(raceToken, knownRaceIdsByName);
        string raceName = TitleCaseToken(raceToken);

        string fallbackName = Path.GetFileNameWithoutExtension(filePath);
        string name = FindCharacterName(tokens, classToken) ?? fallbackName;
        if (IgnoredDisplayTokens.Contains(name))
            name = FindNameFromFileStem(tokens, fallbackName) ?? fallbackName;
        int anchorOffset = FindAnchorOffset(orderedSpans, name, classToken, raceToken);
        string? labeledPlayerName = FindValueAfterKeys(orderedTokens, "player", "player name");
        string? heuristicPlayerName = FindLikelyPlayerName(orderedTokens, name, classToken, raceToken, knownTerms);
        string playerName = labeledPlayerName ?? string.Empty;
        string party = FindValueAfterKeys(orderedTokens, "party", "group") ?? string.Empty;

        var notes = new List<string>
        {
            $"Imported from XPX file: {Path.GetFileName(filePath)}",
            "XPX import is experimental; verify level, abilities, and inventory details.",
        };

        if (hasNonCharacterMarker)
            notes.Add("XPX includes helper/object marker tokens; parsed as character using other detected fields.");

        if (string.IsNullOrWhiteSpace(labeledPlayerName) && !string.IsNullOrWhiteSpace(heuristicPlayerName))
            notes.Add($"XPX low-confidence player name candidate (not auto-applied): {heuristicPlayerName}");

        foreach (string token in tokens.Where(t => !string.Equals(t, name, StringComparison.OrdinalIgnoreCase)
                                                   && !string.Equals(t, classToken, StringComparison.OrdinalIgnoreCase)
                                                   && !IgnoredDisplayTokens.Contains(t))
                                     .Take(3))
        {
            notes.Add($"XPX note: {token}");
        }

        var unresolved = DetectUnresolvedReferences(tokens, knownTerms, name, classToken);
        if (unresolved.Count > 0)
            notes.Add("XPX unresolved references: " + string.Join(", ", unresolved));

        var semanticTokens = BuildSemanticReferenceTokens(tokens);

        var abilities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["str"] = 10,
            ["dex"] = 10,
            ["con"] = 10,
            ["int"] = 10,
            ["wis"] = 10,
            ["cha"] = 10,
        };

        var usedNumericOffsets = new HashSet<int>();

        CoreBlockResult coreBlock = TryExtractCoreBlock(bytes, orderedSpans);

        int? levelFromLabel = TryParseFirstIntForKeys(orderedTokens, 1, 20, "level", "lvl", "lv");
        int? levelFromBinaryLabel = TryParseFirstIntNearKeySpans(bytes, orderedSpans, 1, 20, "level", "lvl", "lv");
        int? levelFromCore = coreBlock.Level;
        int? levelFromBonusBlock = TryExtractLevelFromFighterBonusBlock(bytes);
        int? levelFromNearest = TryExtractNearestValue(bytes, anchorOffset, 1024, 1, 20, usedNumericOffsets);
        int? levelFromPromotedV7 = (!levelFromLabel.HasValue
                                    && !levelFromBinaryLabel.HasValue
                                    && levelFromCore == 1
                                    && levelFromBonusBlock is > 1 and <= 20)
            ? levelFromBonusBlock
            : null;
        int level = levelFromLabel ?? levelFromBinaryLabel ?? levelFromPromotedV7 ?? levelFromCore ?? 1;
        if (!levelFromLabel.HasValue && levelFromBinaryLabel.HasValue)
            notes.Add("XPX parsed level from binary label marker.");
        if (levelFromPromotedV7.HasValue)
            notes.Add($"XPX promoted level from fighter bonus block: {levelFromPromotedV7.Value}");
        if (!levelFromLabel.HasValue && !levelFromCore.HasValue && levelFromNearest.HasValue)
            notes.Add($"XPX low-confidence level candidate (not auto-applied): {levelFromNearest.Value}");

        int textualAbilityCount = TryParseAbilityScoresFromKeys(orderedTokens, abilities, out int exceptionalStrengthFromText);
        int binaryLabeledAbilityCount = TryParseAbilityScoresFromKeySpans(bytes, orderedSpans, abilities);
        int exceptionalStrength = exceptionalStrengthFromText;
        var subAbilities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (TryExtractTripletAbilityBlockInt32(bytes, out var tripletBlock))
        {
            abilities["str"] = tripletBlock.BaseAbilities[0];
            abilities["dex"] = tripletBlock.BaseAbilities[1];
            abilities["con"] = tripletBlock.BaseAbilities[2];
            abilities["int"] = tripletBlock.BaseAbilities[3];
            abilities["wis"] = tripletBlock.BaseAbilities[4];
            abilities["cha"] = tripletBlock.BaseAbilities[5];
            subAbilities = tripletBlock.SubAbilities;
            exceptionalStrength = tripletBlock.ExceptionalStrength > 0
                ? tripletBlock.ExceptionalStrength
                : exceptionalStrengthFromText;
            notes.Add($"XPX parsed ability scores from triplet block at 0x{tripletBlock.Offset:X}.");
            if (exceptionalStrength > 0)
                notes.Add($"XPX parsed exceptional strength percentile: {exceptionalStrength:D2}.");
        }
        else if (textualAbilityCount >= 4)
        {
            notes.Add($"XPX parsed {textualAbilityCount} ability scores from labeled tokens.");
        }
        else if (binaryLabeledAbilityCount >= 4)
        {
            notes.Add($"XPX parsed {binaryLabeledAbilityCount} ability scores from binary label markers.");
        }
        else if (coreBlock.Abilities is { Length: 6 })
        {
            abilities["str"] = coreBlock.Abilities[0];
            abilities["dex"] = coreBlock.Abilities[1];
            abilities["con"] = coreBlock.Abilities[2];
            abilities["int"] = coreBlock.Abilities[3];
            abilities["wis"] = coreBlock.Abilities[4];
            abilities["cha"] = coreBlock.Abilities[5];
            notes.Add("XPX parsed ability scores from structural core block.");
        }
        else if (TryExtractAbilityBlock(bytes, out var extractedScores, out int abilityOffset))
        {
            notes.Add($"XPX low-confidence ability candidate at 0x{abilityOffset:X} (not auto-applied): STR {extractedScores[0]}, DEX {extractedScores[1]}, CON {extractedScores[2]}, INT {extractedScores[3]}, WIS {extractedScores[4]}, CHA {extractedScores[5]}.");

            if (!levelFromLabel.HasValue && !levelFromCore.HasValue && TryExtractLevel(bytes, abilityOffset, out int parsedLevel))
                notes.Add($"XPX low-confidence level candidate from ability block (not auto-applied): {parsedLevel}");
        }

        int? hpFromLabel = TryParseFirstIntForKeys(orderedTokens, 1, 200, "hp", "hit points", "hitpoints");
        int? hpFromBinaryLabel = TryParseFirstIntNearKeySpans(bytes, orderedSpans, 1, 200, "hp", "hit points", "hitpoints");
        int? hpFromCore = coreBlock.HitPoints;
        int? hpFromNearest = TryExtractNearestValue(bytes, anchorOffset, 2048, 1, 200, usedNumericOffsets);
        int hitPoints = hpFromLabel ?? hpFromBinaryLabel ?? hpFromCore ?? 8;
        if (!hpFromLabel.HasValue
            && !hpFromBinaryLabel.HasValue
            && hpFromCore.HasValue
            && levelFromPromotedV7.HasValue
            && classIds.Contains("fighter", StringComparer.OrdinalIgnoreCase)
            && abilities.TryGetValue("con", out int conScore)
            && conScore >= 15)
        {
            hitPoints = hpFromCore.Value + levelFromPromotedV7.Value;
            notes.Add($"XPX adjusted HP using fighter CON bonus and TSRP3 V7 level: {hitPoints}");
        }
        if (!hpFromLabel.HasValue && hpFromBinaryLabel.HasValue)
            notes.Add("XPX parsed HP from binary label marker.");
        if (!hpFromLabel.HasValue && !hpFromCore.HasValue && hpFromNearest.HasValue)
            notes.Add($"XPX low-confidence HP candidate (not auto-applied): {hpFromNearest.Value}");
        int armorClass = TryParseFirstIntForKeys(orderedTokens, -10, 10, "ac", "armor class")
            ?? 10;
        int thac0 = TryParseFirstIntForKeys(orderedTokens, 1, 25, "thac0", "thaco")
            ?? 20;
        int movement = TryParseFirstIntForKeys(orderedTokens, 3, 24, "movement", "move")
            ?? 12;
        int? xpFromLabel = TryParseFirstIntForKeys(orderedTokens, 0, 2_000_000, "xp", "experience", "experience points");
        int? xpFromBinaryLabel = TryParseFirstIntNearKeySpans(bytes, orderedSpans, 0, 2_000_000, "xp", "experience", "experience points");
        int? xpFromCore = coreBlock.ExperiencePoints;
        int? xpFromShiftedSocial = (!xpFromLabel.HasValue
                                    && !xpFromBinaryLabel.HasValue
                        && levelFromPromotedV7.HasValue
                                    && xpFromCore is > 0 and < 1_000)
            ? TryExtractShiftedSocialXp(bytes, orderedSpans, xpFromCore.Value)
            : null;
        int? xpFromNearest = TryExtractNearestValue(bytes, anchorOffset, 4096, 1, 2_000_000, usedNumericOffsets);
        int xp = xpFromLabel ?? xpFromBinaryLabel ?? xpFromShiftedSocial ?? xpFromCore ?? 0;
        if (!xpFromLabel.HasValue && xpFromBinaryLabel.HasValue)
            notes.Add("XPX parsed XP from binary label marker.");
        if (xpFromShiftedSocial.HasValue)
            notes.Add($"XPX promoted XP from shifted social block value: {xpFromShiftedSocial.Value}");
        if (!xpFromLabel.HasValue && !xpFromCore.HasValue && xpFromNearest.HasValue)
            notes.Add($"XPX low-confidence XP candidate (not auto-applied): {xpFromNearest.Value}");

        int pp = TryParseFirstIntForKeys(orderedTokens, 0, 100_000, "pp", "platinum", "platinum pieces") ?? 0;
        int gp = TryParseFirstIntForKeys(orderedTokens, 0, 100_000, "gp", "gold", "gold pieces") ?? 0;
        int sp = TryParseFirstIntForKeys(orderedTokens, 0, 100_000, "sp", "silver", "silver pieces") ?? 0;
        int cp = TryParseFirstIntForKeys(orderedTokens, 0, 100_000, "cp", "copper", "copper pieces") ?? 0;

        var spellIds = MatchTokenValues(semanticTokens, catalogs?.SpellIdsByName);
        var nwpIds = MatchTokenValues(semanticTokens, catalogs?.NwpIdsByName);
        var traitIds = MatchTokenValues(semanticTokens, catalogs?.TraitIdsByName);
        var disadvantageIds = MatchTokenValues(semanticTokens, catalogs?.DisadvantageIdsByName);
        var weaponIds = MatchTokenValues(semanticTokens, catalogs?.WeaponIdsByName);
        var racialAbilityIds = MatchTokenValues(semanticTokens, catalogs?.RacialAbilityIdsByName);
        var classAbilityIds = MatchTokenValues(semanticTokens, catalogs?.ClassAbilityIdsByName);
        var equipment = MatchTokenValues(semanticTokens, catalogs?.EquipmentByName)
            .Select(kv => kv.Value)
            .ToList();
        string characterMode = DetermineCharacterMode(tokens, semanticTokens, nwpIds.Values, catalogs?.PlayersOptionNwpIds);
        if (string.Equals(characterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            notes.Add("XPX inferred mode: Player's Option.");

        var equipmentSelections = equipment.Select(item => new EquipmentSelection
        {
            ItemId = item.Id,
            Category = item.Categories.FirstOrDefault() ?? string.Empty,
            ItemName = item.Name,
            Quantity = 1,
            IsArmor = item.IsArmor,
            ArmorClassValue = item.ArmorClassValue,
            RogueArmorProfile = string.IsNullOrWhiteSpace(item.RogueArmorProfile) ? "no_armor" : item.RogueArmorProfile,
            IsShield = item.IsShield,
            IsWeapon = item.IsWeapon,
            WeaponSpeed = item.WeaponSpeed,
            WeaponDamageSmallMedium = item.WeaponDamageSmallMedium,
            WeaponDamageLarge = item.WeaponDamageLarge,
            WeaponType = item.WeaponType,
            WeaponSize = item.WeaponSize,
            WeaponAttackBonus = item.WeaponAttackBonus,
            WeaponDamageBonus = item.WeaponDamageBonus,
            WeaponThac0Modifier = item.WeaponThac0Modifier,
            CostCopperEach = item.CostCopper,
            CostSilverEach = item.CostSilver,
            CostGoldEach = item.CostGold,
            SizeClassEach = item.SizeClass,
            WeightEach = item.Weight,
        }).ToList();

        string equippedArmorId = equipment.FirstOrDefault(x => x.IsArmor && !x.IsShield)?.Id ?? string.Empty;
        string equippedShieldId = equipment.FirstOrDefault(x => x.IsShield)?.Id ?? string.Empty;
        string equippedWeaponId = equipment.FirstOrDefault(x => x.IsWeapon)?.Id ?? string.Empty;

        if (spellIds.Count > 0)
            notes.Add($"XPX matched spells: {spellIds.Count}");
        if (nwpIds.Count > 0)
            notes.Add($"XPX matched NWPs: {nwpIds.Count}");
        if (traitIds.Count > 0)
            notes.Add($"XPX matched traits: {traitIds.Count}");
        if (disadvantageIds.Count > 0)
            notes.Add($"XPX matched disadvantages: {disadvantageIds.Count}");
        if (weaponIds.Count > 0)
            notes.Add($"XPX matched weapon proficiencies: {weaponIds.Count}");
        if (racialAbilityIds.Count > 0)
            notes.Add($"XPX matched racial abilities: {racialAbilityIds.Count}");
        if (classAbilityIds.Count > 0)
            notes.Add($"XPX matched class abilities: {classAbilityIds.Count}");
        if (equipmentSelections.Count > 0)
            notes.Add($"XPX matched equipment items: {equipmentSelections.Count}");

        var weaponProficiencies = weaponIds
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WeaponProficiencySelection
            {
                ProficiencyId = g.Key,
                ProficiencyType = "individual",
                DisplayName = g.First().Key,
            })
            .ToList();

        if (weaponProficiencies.Count == 0)
        {
            foreach (var weaponItem in equipmentSelections.Where(e => e.IsWeapon))
            {
                weaponProficiencies.Add(new WeaponProficiencySelection
                {
                    ProficiencyId = string.IsNullOrWhiteSpace(weaponItem.ItemId)
                        ? NormalizeKey(weaponItem.ItemName)
                        : weaponItem.ItemId,
                    ProficiencyType = "individual",
                    DisplayName = weaponItem.ItemName,
                });
            }

            if (weaponProficiencies.Count > 0)
                notes.Add($"XPX inferred weapon proficiencies from equipped weapons: {weaponProficiencies.Count}");
        }
        else
        {
            var existingProfIds = new HashSet<string>(
                weaponProficiencies
                    .Select(wp => wp.ProficiencyId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var weaponItem in equipmentSelections.Where(e => e.IsWeapon))
            {
                string inferredId = string.IsNullOrWhiteSpace(weaponItem.ItemId)
                    ? NormalizeKey(weaponItem.ItemName)
                    : weaponItem.ItemId;

                if (!existingProfIds.Add(inferredId))
                    continue;

                weaponProficiencies.Add(new WeaponProficiencySelection
                {
                    ProficiencyId = inferredId,
                    ProficiencyType = "individual",
                    DisplayName = weaponItem.ItemName,
                });
            }
        }

        bool hasSevereDisadvantageMarker = tokens.Any(t => string.Equals(NormalizeKey(t), "severe", StringComparison.OrdinalIgnoreCase));
        var selectedDisadvantageSeverities = disadvantageIds.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                id => id,
                _ => hasSevereDisadvantageMarker ? "severe" : "moderate",
                StringComparer.OrdinalIgnoreCase);

        var supplementalEquipmentNames = unresolved
            .Where(IsLikelySupplementalEquipment)
            .ToList();

        var character = new CharacterSheet
        {
            Name = name,
            PlayerName = playerName,
            Party = party,
            ClassId = classId,
            ClassName = className,
            ClassMode = (classToken.Contains('/') || classIds.Count > 1) ? "multiclass" : "single",
            ClassIds = classIds,
            RaceId = raceId,
            RaceName = raceName,
            CharacterMode = characterMode,
            Level = level,
            ExperiencePoints = xp,
            Abilities = abilities,
            HitPoints = hitPoints,
            CurrentHitPoints = hitPoints,
            BaseHitPoints = hitPoints,
            ArmorClass = armorClass,
            BaseArmorClass = armorClass,
            Thac0 = thac0,
            Movement = movement,
            BaseMovement = movement,
            PlatinumPieces = pp,
            GoldPieces = gp,
            SilverPieces = sp,
            CopperPieces = cp,
            Equipment = equipmentSelections
                .Select(x => x.ItemName)
                .Concat(supplementalEquipmentNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EquipmentSelections = equipmentSelections,
            EquippedArmorId = equippedArmorId,
            EquippedShieldId = equippedShieldId,
            EquippedWeaponId = equippedWeaponId,
            EquippedMainWeaponId = equippedWeaponId,
            Traits = traitIds.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SelectedTraitIds = traitIds.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Disadvantages = disadvantageIds.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SelectedDisadvantageSeverities = selectedDisadvantageSeverities,
            SelectedRacialAbilityIds = racialAbilityIds.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SelectedClassAbilityIds = classAbilityIds.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SubAbilities = subAbilities,
            ExceptionalStrength = exceptionalStrength,
            TrackedSpellIds = spellIds.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            NonweaponProficiencyIds = nwpIds.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            WeaponProficiencies = weaponProficiencies,
            Notes = notes,
            LastModified = DateTime.Now,
        };

        if (nwpIds.Count > 0)
            character.NonweaponProficiencies = nwpIds.Keys.ToList();

        return new XpxImportResult(character, unresolved);
    }

    private static bool LooksLikeXpx(byte[] data)
    {
        if (data.Length < 20)
            return false;

        bool hasLeadSentinel = data[0] == 0xFF && data[1] == 0xFF && data[2] == 0xFF && data[3] == 0xFF;
        string header = Encoding.ASCII.GetString(data, 4, Math.Min(16, data.Length - 4));
        return hasLeadSentinel && header.Contains("TSRP3", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindClassToken(List<string> tokens, IReadOnlyDictionary<string, string>? knownClassIdsByName)
    {
        string? bestToken = null;
        int bestKnownCount = 0;

        foreach (string token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var parts = token.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                continue;

            int knownCount = parts.Count(p => ResolveClassId(p.Trim(), knownClassIdsByName) is not null);
            if (knownCount == 0)
                continue;

            bool better = knownCount > bestKnownCount
                || (knownCount == bestKnownCount && bestToken is not null && parts.Length > bestToken.Split('/').Length)
                || bestToken is null;
            if (better)
            {
                bestToken = token;
                bestKnownCount = knownCount;
            }
        }

        return bestToken;
    }

    private static List<string> ParseClassIds(string classToken, IReadOnlyDictionary<string, string>? knownClassIdsByName)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string part in classToken.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string token = part.Trim();
            string? classId = ResolveClassId(token, knownClassIdsByName);
            if (string.IsNullOrWhiteSpace(classId))
                continue;

            if (seen.Add(classId))
                ids.Add(classId);
        }

        if (ids.Count == 0)
            ids.Add("fighter");

        return ids;
    }

    private static string? ResolveClassId(string token, IReadOnlyDictionary<string, string>? knownClassIdsByName)
    {
        if (ClassIdByName.TryGetValue(token, out var builtInId))
            return builtInId;

        if (knownClassIdsByName is null)
            return null;

        if (knownClassIdsByName.TryGetValue(token, out var knownId))
            return knownId;

        return null;
    }

    private static string? FindRaceToken(List<string> tokens, IReadOnlyDictionary<string, string>? knownRaceIdsByName)
    {
        foreach (string token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (ResolveRaceId(token, knownRaceIdsByName) != "human"
                || token.Equals("human", StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }
        }

        return null;
    }

    private static string ResolveRaceId(string token, IReadOnlyDictionary<string, string>? knownRaceIdsByName)
    {
        if (RaceIdByName.TryGetValue(token, out var builtInId))
            return builtInId;

        if (knownRaceIdsByName is not null && knownRaceIdsByName.TryGetValue(token, out var knownId))
            return knownId;

        return "human";
    }

    private static string BuildClassName(List<string> classIds, string classToken)
    {
        if (classIds.Count <= 1)
            return classToken;

        return string.Join("/", classIds.Select(TitleCaseClassId));
    }

    private static string? FindCharacterName(List<string> tokens, string classToken)
    {
        int classIndex = tokens.FindIndex(t => string.Equals(t, classToken, StringComparison.OrdinalIgnoreCase));
        if (classIndex > 0)
        {
            for (int i = classIndex - 1; i >= 0; i--)
            {
                string candidate = tokens[i];
                if (IsLikelyCharacterName(candidate))
                    return candidate;
            }
        }

        return tokens.FirstOrDefault(IsLikelyCharacterName);
    }

    private static string? FindNameFromFileStem(List<string> tokens, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(fallbackName))
            return null;

        string stemLetters = new string(fallbackName.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(stemLetters))
            return null;

        foreach (string token in tokens)
        {
            if (!IsLikelyCharacterName(token))
                continue;

            string tokenLetters = new string(token.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            if (tokenLetters.Length < 3)
                continue;
            if (tokenLetters.Contains(stemLetters, StringComparison.OrdinalIgnoreCase)
                || stemLetters.Contains(tokenLetters, StringComparison.OrdinalIgnoreCase)
                || tokenLetters.StartsWith(stemLetters[..Math.Min(5, stemLetters.Length)], StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }
        }

        return null;
    }

    private static bool IsLikelyCharacterName(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (token.Length < 3 || token.Length > 64)
            return false;

        if (IgnoredDisplayTokens.Contains(token))
            return false;

        if (ClassIdByName.ContainsKey(token))
            return false;

        if (token.Contains("TSRP3", StringComparison.OrdinalIgnoreCase))
            return false;

        if (token.Contains("ClassesAllowed", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!token.Any(char.IsLetter))
            return false;

        return true;
    }

    private static string? FindLikelyPlayerName(
        List<string> orderedTokens,
        string characterName,
        string classToken,
        string raceToken,
        IReadOnlyCollection<string>? knownTerms)
    {
        int characterIndex = orderedTokens.FindIndex(t => string.Equals(t, characterName, StringComparison.OrdinalIgnoreCase));
        int scanLimit = characterIndex > 0 ? characterIndex : Math.Min(orderedTokens.Count, 20);

        HashSet<string>? known = null;
        if (knownTerms is not null && knownTerms.Count > 0)
        {
            known = new HashSet<string>(knownTerms
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(NormalizeKey),
                StringComparer.OrdinalIgnoreCase);
        }

        for (int i = 0; i < scanLimit; i++)
        {
            string token = orderedTokens[i];
            if (string.IsNullOrWhiteSpace(token))
                continue;
            if (string.Equals(token, characterName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(token, classToken, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(token, raceToken, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IgnoredDisplayTokens.Contains(token))
                continue;
            if (token.Length < 3 || token.Length > 40)
                continue;
            if (!token.All(ch => char.IsLetter(ch) || ch is ' ' or '-' or '\''))
                continue;
            if (!token.Any(char.IsLetter))
                continue;
            if (token.Count(char.IsWhiteSpace) > 2)
                continue;

            string normalized = NormalizeKey(token);
            if (known is not null && known.Contains(normalized))
                continue;
            if (normalized.Contains("bonus", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("battle", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("class", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("skill", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("fled", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("disaster", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("ff", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var words = token.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0 || words.Length > 3)
                continue;

            bool allNameWords = words.All(w => w.Length >= 2 && char.IsUpper(w[0]) && w.Skip(1).All(char.IsLetter));
            if (!allNameWords)
                continue;

            return token;
        }

        return null;
    }

    private static string TitleCaseClassId(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return classId;

        return classId.Length == 1
            ? classId.ToUpperInvariant()
            : char.ToUpperInvariant(classId[0]) + classId[1..].ToLowerInvariant();
    }

    private static string TitleCaseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return token;

        var parts = token.Split(new[] { '/', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return token;

        return string.Join(" ", parts.Select(p => p.Length == 1
            ? p.ToUpperInvariant()
            : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    private static List<string> DetectUnresolvedReferences(
        List<string> tokens,
        IReadOnlyCollection<string>? knownTerms,
        string characterName,
        string classToken)
    {
        if (knownTerms is null || knownTerms.Count == 0)
            return new List<string>();

        var known = new HashSet<string>(knownTerms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(NormalizeKey),
            StringComparer.OrdinalIgnoreCase);

        var knownWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string term in knownTerms.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            foreach (string part in term.Split(new[] { ' ', ',', '/', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string word = NormalizeKey(part);
                if (word.Length >= 4)
                    knownWords.Add(word);
            }
        }

        var unresolved = new List<string>();
        foreach (string token in tokens)
        {
            if (!LooksLikeReferenceToken(token))
                continue;
            if (string.Equals(token, characterName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(token, classToken, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IgnoredDisplayTokens.Contains(token) || NonCharacterMarkers.Contains(token))
                continue;

            string normalized = NormalizeKey(token);
            if (known.Contains(normalized))
                continue;
            if (ShouldIgnoreUnresolvedToken(token, normalized, knownWords))
                continue;

            if (!unresolved.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                unresolved.Add(token);
        }

        return unresolved.Take(8).ToList();
    }

    private static bool ShouldIgnoreUnresolvedToken(string token, string normalized, HashSet<string> knownWords)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        if (normalized.StartsWith("ff", StringComparison.OrdinalIgnoreCase) && normalized.Length <= 3)
            return true;

        if (normalized.Contains("bonus", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized is "weaponspecialization"
            or "limitedarmor"
            or "meleecombat"
            or "lightsleeping"
            or "dangersense"
            or "keeneyesight"
            or "phobia"
            or "enclosedspaces")
        {
            return true;
        }

        if (normalized is "battle" or "handthrowing" or "closetotheearth")
            return true;

        if (normalized is "severe"
            or "cstringarray"
            or "cinventorybundle"
            or "cpart"
            or "dryrations"
            or "flintandsteel"
            or "crossbowofspeed"
            or "goldpiece"
            or "goldpieces"
            or "silverpiece"
            or "silverpieces"
            or "copperpiece"
            or "copperpieces")
        {
            return true;
        }

        if (normalized is "intelligencereason")
            return true;

        if (normalized is "source"
            or "sources"
            or "completebarbarianshandbook"
            or "completebookofhumanoids"
            or "wisdomintuition"
            or "constitution"
            or "constitutionfitness"
            or "lightsleeper")
        {
            return true;
        }

        if (normalized is "coastaloutlines"
            or "hazards"
            or "forexample"
            or "infront"
            or "totheright"
            or "overhead"
            or "andsoon"
            or "aconcealedlion")
        {
            return true;
        }

        bool hasLetter = token.Any(char.IsLetter);
        bool hasUpper = token.Any(char.IsUpper);
        if (hasLetter && !hasUpper)
        {
            int lowerPhraseWordCount = token.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            if (lowerPhraseWordCount >= 2)
                return true;
        }

        int wordCount = token.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        if (wordCount >= 5)
            return true;

        if (token.Contains('.', StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.EndsWith("week", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("weeks", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("day", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("days", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (knownWords.Contains(normalized))
            return true;

        return false;
    }

    private static bool IsLikelySupplementalEquipment(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string normalized = NormalizeKey(token);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized is "severe" or "cstringarray" or "cinventorybundle" or "copperpieces")
            return false;

        if (normalized.All(char.IsDigit))
            return false;

        if (normalized.EndsWith("week", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("weeks", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("day", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("days", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static List<string> BuildSemanticReferenceTokens(IEnumerable<string> tokens)
    {
        var result = new HashSet<string>(tokens.Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);

        bool hasPhobia = false;
        bool hasEnclosedSpaces = false;
        foreach (string token in result.ToList())
        {
            string normalized = NormalizeKey(token);
            switch (normalized)
            {
                case "lightsleeping":
                    result.Add("Light Sleeper");
                    break;
                case "limitedarmor":
                    result.Add("Restriction - Limited armor");
                    break;
                case "meleecombat":
                    result.Add("Melee Combat bonus");
                    break;
                case "weaponspecialization":
                    result.Add("Weapon Specialization");
                    break;
                case "dangersense":
                    result.Add("Danger sense");
                    break;
                case "keeneyesight":
                    result.Add("Keen eyesight");
                    break;
                case "axebattle":
                    result.Add("Battle Axe");
                    result.Add("Battle axe");
                    result.Add("Axe, Battle");
                    result.Add("Axe, battle");
                    break;
                case "axehandthrowing":
                    result.Add("Hand/Throwing Axe");
                    result.Add("Hand/Throwing axe");
                    result.Add("Axe, Hand/Throwing");
                    result.Add("Axe, hand/throwing");
                    result.Add("Hand Axe");
                    break;
                case "crossbowlight":
                    result.Add("Light Crossbow");
                    break;
                case "crossbowofspeed":
                case "crossbowofspeedlight":
                    result.Add("Light Crossbow");
                    result.Add("Crossbow, light");
                    break;
                case "crossbowlightquarrel":
                    result.Add("Crossbow Quarrel");
                    result.Add("Light Crossbow");
                    break;
                case "cestus":
                    result.Add("Cestus");
                    break;
                case "phobia":
                    hasPhobia = true;
                    break;
                case "enclosedspaces":
                    hasEnclosedSpaces = true;
                    break;
            }

            if (normalized.Contains("axebonus5", StringComparison.OrdinalIgnoreCase))
                result.Add("Axe bonus (5)");
            if (normalized.Contains("closetotheearth5", StringComparison.OrdinalIgnoreCase))
                result.Add("Close to the earth (5)");
            if (normalized.Contains("crossbowbonus5", StringComparison.OrdinalIgnoreCase))
                result.Add("Crossbow bonus (5)");
            if (normalized.Contains("hitpointbonus10", StringComparison.OrdinalIgnoreCase))
                result.Add("Hit point bonus (10)");
            if (normalized.Contains("infravision10", StringComparison.OrdinalIgnoreCase))
                result.Add("Infravision (10)");
            if (normalized.Contains("meleecombat10", StringComparison.OrdinalIgnoreCase))
                result.Add("Melee combat (10)");
            if (normalized.Contains("defensebonus10", StringComparison.OrdinalIgnoreCase))
                result.Add("Defense bonus (10)");
            if (normalized.Contains("magicresistance10", StringComparison.OrdinalIgnoreCase))
                result.Add("Magic resistance (10)");
            if (normalized.Contains("weaponspecialization5", StringComparison.OrdinalIgnoreCase))
                result.Add("Weapon specialization (5)");
            if (normalized.Contains("limitedarmor15", StringComparison.OrdinalIgnoreCase))
                result.Add("Limited armor (15)");
        }

        if (hasPhobia && hasEnclosedSpaces)
        {
            result.Add("Phobia: Enclosed Spaces");
            result.Add("Claustrophobia");
        }

        return result.ToList();
    }

    private static string DetermineCharacterMode(
        IEnumerable<string> rawTokens,
        IEnumerable<string> semanticTokens,
        IEnumerable<string> matchedNwpIds,
        IReadOnlySet<string>? playersOptionNwpIds)
    {
        if (playersOptionNwpIds is not null)
        {
            foreach (string nwpId in matchedNwpIds)
            {
                if (playersOptionNwpIds.Contains(nwpId))
                    return "players_option";
            }
        }

        foreach (string token in rawTokens.Concat(semanticTokens))
        {
            string normalized = NormalizeKey(token);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (PlayersOptionSignals.Contains(normalized))
                return "players_option";
        }

        return "players_option";
    }

    private static bool LooksLikeReferenceToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        if (token.Length < 4 || token.Length > 60)
            return false;
        if (!token.Any(char.IsLetter))
            return false;
        if (token.StartsWith("TSRP", StringComparison.OrdinalIgnoreCase))
            return false;
        if (token.Contains("class", StringComparison.OrdinalIgnoreCase)
            && token.Contains("allowed", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string NormalizeKey(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private static string? FindValueAfterKeys(List<string> orderedTokens, params string[] keys)
    {
        if (orderedTokens.Count == 0)
            return null;

        var normalizedKeys = new HashSet<string>(keys.Select(NormalizeKey), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < orderedTokens.Count; i++)
        {
            string token = orderedTokens[i];
            if (!normalizedKeys.Contains(NormalizeKey(token)))
                continue;

            for (int look = 1; look <= 3 && i + look < orderedTokens.Count; look++)
            {
                string value = orderedTokens[i + look].Trim();
                if (!LooksLikeReferenceToken(value))
                    continue;
                return value;
            }
        }

        return null;
    }

    private static int? TryParseFirstIntForKeys(List<string> orderedTokens, int min, int max, params string[] keys)
    {
        if (orderedTokens.Count == 0)
            return null;

        var normalizedKeys = new HashSet<string>(keys.Select(NormalizeKey), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < orderedTokens.Count; i++)
        {
            if (!normalizedKeys.Contains(NormalizeKey(orderedTokens[i])))
                continue;

            for (int look = 1; look <= 3 && i + look < orderedTokens.Count; look++)
            {
                if (!TryParseLooseInt(orderedTokens[i + look], out int value))
                    continue;
                if (value < min || value > max)
                    continue;
                return value;
            }
        }

        return null;
    }

    private static int? TryParseFirstIntNearKeySpans(
        byte[] data,
        List<TokenSpan> spans,
        int min,
        int max,
        params string[] keys)
    {
        if (data.Length == 0 || spans.Count == 0)
            return null;

        var normalizedKeys = new HashSet<string>(keys.Select(NormalizeKey), StringComparer.OrdinalIgnoreCase);

        foreach (TokenSpan span in spans)
        {
            if (!normalizedKeys.Contains(NormalizeKey(span.Token)))
                continue;

            if (TryReadNearestNumericForward(data, span.End + 1, min, max, 32, out int value, out _))
                return value;
        }

        return null;
    }

    private static bool TryReadNearestNumericForward(
        byte[] data,
        int start,
        int min,
        int max,
        int searchBytes,
        out int value,
        out int offset)
    {
        value = 0;
        offset = -1;

        if (data.Length == 0)
            return false;

        int from = Math.Max(0, start);
        int to = Math.Min(data.Length - 1, from + Math.Max(1, searchBytes));
        int bestDistance = int.MaxValue;
        int bestValue = 0;
        int bestOffset = -1;

        void Consider(int candidate, int at)
        {
            if (candidate < min || candidate > max)
                return;

            int distance = Math.Max(0, at - from);
            if (distance >= bestDistance)
                return;

            bestDistance = distance;
            bestValue = candidate;
            bestOffset = at;
        }

        for (int at = from; at <= to; at++)
        {
            if (at + 4 <= data.Length)
                Consider(BitConverter.ToInt32(data, at), at);
            if (at + 2 <= data.Length)
                Consider(BitConverter.ToInt16(data, at), at);
            Consider(data[at], at);
        }

        if (bestOffset < 0)
            return false;

        value = bestValue;
        offset = bestOffset;
        return true;
    }

    private static bool TryParseLooseInt(string token, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string cleaned = token.Trim().Replace(",", string.Empty);
        return int.TryParse(cleaned, out value);
    }

    private static int FindAnchorOffset(List<TokenSpan> spans, string name, string classToken, string raceToken)
    {
        foreach (string key in new[] { name, classToken, raceToken })
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            TokenSpan? span = spans.FirstOrDefault(s => string.Equals(s.Token, key, StringComparison.OrdinalIgnoreCase));
            if (span is not null)
                return span.Start;
        }

        return spans.Count > 0 ? spans[0].Start : 0;
    }

    private static int? TryExtractNearestValue(
        byte[] data,
        int anchorOffset,
        int windowSize,
        int min,
        int max,
        HashSet<int>? usedOffsets = null)
    {
        if (data.Length < 2)
            return null;

        int start = Math.Max(0, anchorOffset - windowSize);
        int end = Math.Min(data.Length - 1, anchorOffset + windowSize);
        int? bestValue = null;
        int bestOffset = -1;
        int bestDistance = int.MaxValue;

        void Consider(int value, int offset)
        {
            if (value < min || value > max)
                return;
            if (usedOffsets is not null && usedOffsets.Contains(offset))
                return;

            int distance = Math.Abs(offset - anchorOffset);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestValue = value;
                bestOffset = offset;
            }
        }

        for (int offset = start; offset <= end - 4; offset++)
        {
            int i32 = BitConverter.ToInt32(data, offset);
            Consider(i32, offset);
        }

        for (int offset = start; offset <= end - 2; offset++)
        {
            int i16 = BitConverter.ToInt16(data, offset);
            Consider(i16, offset);
        }

        for (int offset = start; offset <= end; offset++)
        {
            Consider(data[offset], offset);
        }

        if (bestValue.HasValue && usedOffsets is not null && bestOffset >= 0)
            usedOffsets.Add(bestOffset);

        return bestValue;
    }

    private static CoreBlockResult TryExtractCoreBlock(byte[] data, List<TokenSpan> orderedSpans)
    {
        TokenSpan? social = orderedSpans.FirstOrDefault(s =>
            s.Token.Contains("class", StringComparison.OrdinalIgnoreCase)
            && (s.Token.Contains("upper", StringComparison.OrdinalIgnoreCase)
                || s.Token.Contains("middle", StringComparison.OrdinalIgnoreCase)
                || s.Token.Contains("lower", StringComparison.OrdinalIgnoreCase)));
        if (social is null)
            return new CoreBlockResult(null, null, null, null);

        int start = Math.Min(data.Length - 1, social.End + 1);
        int end = Math.Min(data.Length - 4, start + 256);
        if (end <= start)
            return new CoreBlockResult(null, null, null, null);

        int? xp = null;
        for (int o = start; o <= Math.Min(end, start + 32); o++)
        {
            int v = BitConverter.ToInt32(data, o);
            if (v is >= 1 and <= 2_000_000)
            {
                xp = v;
                break;
            }
        }

        int? level = null;
        int[]? abilities = null;
        int? hp = null;

        for (int o = start; o <= Math.Min(end, start + 96); o++)
        {
            int v = BitConverter.ToInt32(data, o);
            if (v is < 1 or > 20)
                continue;

            var seq = new List<int>();
            for (int i = 1; i <= 12 && o + (i * 4) <= end; i++)
                seq.Add(BitConverter.ToInt32(data, o + (i * 4)));

            int abilityish = seq.Count(x => x is >= 3 and <= 25);
            if (abilityish < 5)
                continue;

            level = v;

            int bestScore = int.MinValue;
            int[]? best = null;
            for (int i = 0; i <= seq.Count - 6; i++)
            {
                int[] candidate = seq.Skip(i).Take(6).ToArray();
                if (candidate.Any(x => x is < 3 or > 25))
                    continue;
                int score = ScoreAbilityCandidate(candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best is not null)
                abilities = best;

            int prev = (o - 4 >= start) ? BitConverter.ToInt32(data, o - 4) : 0;
            if (prev is >= 1 and <= 300)
                hp = prev;

            break;
        }

        return new CoreBlockResult(xp, level, hp, abilities);
    }

    private static int? TryExtractLevelFromFighterBonusBlock(byte[] data)
    {
        if (data.Length < 64)
            return null;

        byte[] marker = Encoding.ASCII.GetBytes("Fighter, Defense bonus");

        for (int i = 0; i <= data.Length - marker.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
            {
                if (data[i + j] != marker[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            int start = Math.Max(0, i - 48);
            int end = Math.Max(start, i - 1);
            int? nearest = null;
            int nearestDistance = int.MaxValue;
            for (int o = start; o <= end && o <= data.Length - 4; o++)
            {
                int v = BitConverter.ToInt32(data, o);
                if (v is < 1 or > 30)
                    continue;

                int distance = i - o;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = v;
                }
            }

            if (nearest.HasValue)
                return nearest;
        }

        return null;
    }

    private static int? TryExtractShiftedSocialXp(byte[] data, List<TokenSpan> orderedSpans, int baseXp)
    {
        TokenSpan? social = orderedSpans.FirstOrDefault(s =>
            s.Token.Contains("class", StringComparison.OrdinalIgnoreCase)
            && (s.Token.Contains("upper", StringComparison.OrdinalIgnoreCase)
                || s.Token.Contains("middle", StringComparison.OrdinalIgnoreCase)
                || s.Token.Contains("lower", StringComparison.OrdinalIgnoreCase)));
        if (social is null)
            return null;

        int best = -1;
        int bestRatioDistance = int.MaxValue;
        for (int o = Math.Max(0, social.End - 2); o <= Math.Min(data.Length - 4, social.End + 3); o++)
        {
            int v = BitConverter.ToInt32(data, o);
            if (v < 1_000 || v > 2_000_000 || v <= baseXp)
                continue;

            double ratio = baseXp <= 0 ? 0 : (double)v / baseXp;
            int ratioDistance = (int)Math.Abs(ratio - 256.0);
            if (ratioDistance < bestRatioDistance)
            {
                bestRatioDistance = ratioDistance;
                best = v;
            }
        }

        return best > 0 ? best : null;
    }

    private static int TryParseAbilityScoresFromKeys(
        List<string> orderedTokens,
        Dictionary<string, int> abilities,
        out int exceptionalStrength)
    {
        int found = 0;
        exceptionalStrength = 0;
        int parsedExceptionalStrength = 0;

        bool TryAssign(string key, string token)
        {
            if (!TryParseAbilityToken(token, out int value, out int exceptional))
                return false;
            if (value is < 3 or > 25)
                return false;

            abilities[key] = value;
            if (key == "str" && exceptional > 0)
                parsedExceptionalStrength = exceptional;
            return true;
        }

        for (int i = 0; i < orderedTokens.Count; i++)
        {
            string key = NormalizeKey(orderedTokens[i]);
            string abilityKey = key switch
            {
                "str" or "strength" => "str",
                "dex" or "dexterity" => "dex",
                "con" or "constitution" => "con",
                "int" or "intelligence" => "int",
                "wis" or "wisdom" => "wis",
                "cha" or "charisma" => "cha",
                _ => string.Empty,
            };

            if (string.IsNullOrWhiteSpace(abilityKey))
                continue;

            bool assigned = false;
            for (int look = 1; look <= 3 && i + look < orderedTokens.Count; look++)
            {
                if (TryAssign(abilityKey, orderedTokens[i + look]))
                {
                    assigned = true;
                    break;
                }
            }

            if (assigned)
                found++;
        }

        exceptionalStrength = parsedExceptionalStrength;
        return found;
    }

    private static bool TryParseAbilityToken(string token, out int value, out int exceptionalStrength)
    {
        exceptionalStrength = 0;
        value = 0;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        string cleaned = token.Trim().Replace(",", string.Empty);
        int slashIndex = cleaned.IndexOf('/');
        if (slashIndex <= 0)
            return int.TryParse(cleaned, out value);

        string left = cleaned.Substring(0, slashIndex).Trim();
        string right = cleaned[(slashIndex + 1)..].Trim();
        if (!int.TryParse(left, out value))
            return false;

        if (right == "00")
        {
            exceptionalStrength = 100;
            return true;
        }

        if (int.TryParse(right, out int parsedExceptional) && parsedExceptional is >= 1 and <= 99)
        {
            exceptionalStrength = parsedExceptional;
            return true;
        }

        return true;
    }

    private static int TryParseAbilityScoresFromKeySpans(
        byte[] data,
        List<TokenSpan> orderedSpans,
        Dictionary<string, int> abilities)
    {
        int found = 0;

        bool TryAssign(string abilityKey, params string[] aliases)
        {
            int? value = TryParseFirstIntNearKeySpans(data, orderedSpans, 3, 25, aliases);
            if (!value.HasValue)
                return false;

            abilities[abilityKey] = value.Value;
            return true;
        }

        if (TryAssign("str", "str", "strength"))
            found++;
        if (TryAssign("dex", "dex", "dexterity"))
            found++;
        if (TryAssign("con", "con", "constitution"))
            found++;
        if (TryAssign("int", "int", "intelligence"))
            found++;
        if (TryAssign("wis", "wis", "wisdom"))
            found++;
        if (TryAssign("cha", "cha", "charisma"))
            found++;

        return found;
    }

    private static Dictionary<string, TValue> MatchTokenValues<TValue>(
        IEnumerable<string> tokens,
        IReadOnlyDictionary<string, TValue>? lookup)
    {
        var matched = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        if (lookup is null || lookup.Count == 0)
            return matched;

        var normalizedLookup = new Dictionary<string, KeyValuePair<string, TValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in lookup)
        {
            string key = NormalizeKey(kv.Key);
            if (string.IsNullOrWhiteSpace(key))
                continue;
            normalizedLookup[key] = kv;
        }

        foreach (string token in tokens)
        {
            string key = NormalizeKey(token);
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!normalizedLookup.TryGetValue(key, out var value))
                continue;

            matched[value.Key] = value.Value;
        }

        return matched;
    }

    private static bool TryExtractAbilityBlock(byte[] data, out int[] scores, out int startOffset)
    {
        if (TryExtractAbilityBlockInt32(data, out scores, out startOffset))
            return true;
        if (TryExtractAbilityBlockInt16(data, out scores, out startOffset))
            return true;
        if (TryExtractAbilityBlockByte(data, out scores, out startOffset))
            return true;

        scores = Array.Empty<int>();
        startOffset = -1;
        return false;
    }

    private static bool TryExtractAbilityBlockInt32(byte[] data, out int[] scores, out int startOffset)
    {
        scores = Array.Empty<int>();
        startOffset = -1;
        if (data.Length < 24)
            return false;

        int bestScore = int.MinValue;
        int[]? bestCandidate = null;
        int bestOffset = -1;

        for (int offset = 64; offset <= data.Length - 24; offset += 4)
        {
            var candidate = new int[6];
            bool valid = true;
            for (int i = 0; i < 6; i++)
            {
                int value = BitConverter.ToInt32(data, offset + (i * 4));
                if (value is < 3 or > 25)
                {
                    valid = false;
                    break;
                }

                candidate[i] = value;
            }

            if (!valid)
                continue;

            int spread = candidate.Max() - candidate.Min();
            int meanDistance = candidate.Sum(v => Math.Abs(v - 12));
            int candidateScore = spread * 10 - meanDistance;

            if (candidateScore > bestScore)
            {
                bestScore = candidateScore;
                bestCandidate = candidate;
                bestOffset = offset;
            }
        }

        if (bestCandidate is null)
            return false;

        scores = bestCandidate;
        startOffset = bestOffset;
        return true;
    }

    private static bool TryExtractTripletAbilityBlockInt32(byte[] data, out TripletAbilityBlockResult result)
    {
        result = new TripletAbilityBlockResult(
            Array.Empty<int>(),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            0,
            -1);

        if (data.Length < (64 + (18 * 4)))
            return false;

        int bestScore = int.MinValue;
        TripletAbilityBlockResult? best = null;

        for (int offset = 32; offset <= data.Length - (18 * 4); offset++)
        {
            var values = new int[18];
            bool valid = true;
            for (int i = 0; i < 18; i++)
            {
                int value = BitConverter.ToInt32(data, offset + (i * 4));
                if (value is < 1 or > 25)
                {
                    valid = false;
                    break;
                }

                values[i] = value;
            }

            if (!valid)
                continue;

            int[] baseScores =
            {
                values[0],
                values[3],
                values[6],
                values[9],
                values[12],
                values[15],
            };

            if (baseScores.Any(v => v is < 3 or > 25))
                continue;

            if (!LooksLikeAbilitySet(baseScores))
                continue;

            var subAbilities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["str_stamina"] = values[1],
                ["str_muscle"] = values[2],
                ["dex_aim"] = values[4],
                ["dex_balance"] = values[5],
                ["con_health"] = values[7],
                ["con_fitness"] = values[8],
                ["int_reason"] = values[10],
                ["int_knowledge"] = values[11],
                ["wis_intuition"] = values[13],
                ["wis_willpower"] = values[14],
                ["cha_leadership"] = values[16],
                ["cha_appearance"] = values[17],
            };

            int exceptionalStrength = 0;
            if (baseScores[0] == 18)
            {
                int[] strCompanions = { values[1], values[2] };
                exceptionalStrength = strCompanions
                    .Where(v => v is > 0 and <= 100 && v != 18)
                    .DefaultIfEmpty(0)
                    .First();

                // XPX commonly stores the exceptional percentile beside STR sub-abilities.
                // When detected, keep both STR sub-ability scores aligned to base STR 18.
                if (exceptionalStrength > 0)
                {
                    subAbilities["str_stamina"] = 18;
                    subAbilities["str_muscle"] = 18;
                }
            }

            int candidateScore = ScoreAbilityCandidate(baseScores)
                                 + (baseScores[0] == 18 ? 4 : 0)
                                 + (exceptionalStrength > 0 ? 6 : 0);

            if (candidateScore <= bestScore)
                continue;

            bestScore = candidateScore;
            best = new TripletAbilityBlockResult(baseScores, subAbilities, exceptionalStrength, offset);
        }

        if (best is null)
            return false;

        result = best;
        return true;
    }

    private static bool TryExtractAbilityBlockInt16(byte[] data, out int[] scores, out int startOffset)
    {
        scores = Array.Empty<int>();
        startOffset = -1;
        if (data.Length < 12)
            return false;

        int bestScore = int.MinValue;
        int[]? bestCandidate = null;
        int bestOffset = -1;

        for (int offset = 64; offset <= data.Length - 12; offset += 2)
        {
            var candidate = new int[6];
            bool valid = true;
            for (int i = 0; i < 6; i++)
            {
                int value = BitConverter.ToInt16(data, offset + (i * 2));
                if (value is < 3 or > 25)
                {
                    valid = false;
                    break;
                }

                candidate[i] = value;
            }

            if (!valid || !LooksLikeAbilitySet(candidate))
                continue;

            int candidateScore = ScoreAbilityCandidate(candidate);
            if (candidateScore > bestScore)
            {
                bestScore = candidateScore;
                bestCandidate = candidate;
                bestOffset = offset;
            }
        }

        if (bestCandidate is null)
            return false;

        scores = bestCandidate;
        startOffset = bestOffset;
        return true;
    }

    private static bool TryExtractAbilityBlockByte(byte[] data, out int[] scores, out int startOffset)
    {
        scores = Array.Empty<int>();
        startOffset = -1;
        if (data.Length < 6)
            return false;

        int bestScore = int.MinValue;
        int[]? bestCandidate = null;
        int bestOffset = -1;

        for (int offset = 64; offset <= data.Length - 6; offset++)
        {
            var candidate = new int[6];
            bool valid = true;
            for (int i = 0; i < 6; i++)
            {
                int value = data[offset + i];
                if (value is < 3 or > 25)
                {
                    valid = false;
                    break;
                }

                candidate[i] = value;
            }

            if (!valid || !LooksLikeAbilitySet(candidate))
                continue;

            int candidateScore = ScoreAbilityCandidate(candidate);
            if (candidateScore > bestScore)
            {
                bestScore = candidateScore;
                bestCandidate = candidate;
                bestOffset = offset;
            }
        }

        if (bestCandidate is null)
            return false;

        scores = bestCandidate;
        startOffset = bestOffset;
        return true;
    }

    private static bool LooksLikeAbilitySet(int[] candidate)
    {
        if (candidate.Length != 6)
            return false;

        int spread = candidate.Max() - candidate.Min();
        int sum = candidate.Sum();
        int unique = candidate.Distinct().Count();

        if (spread < 2 || spread > 18)
            return false;
        if (sum < 45 || sum > 120)
            return false;
        if (unique < 3)
            return false;

        return true;
    }

    private static int ScoreAbilityCandidate(int[] candidate)
    {
        int spread = candidate.Max() - candidate.Min();
        int meanDistance = candidate.Sum(v => Math.Abs(v - 12));
        int highStats = candidate.Count(v => v >= 15);
        return (spread * 8) + (highStats * 6) - meanDistance;
    }

    private static bool TryExtractLevel(byte[] data, int abilityOffset, out int level)
    {
        level = 1;
        if (abilityOffset <= 0)
            return false;

        int windowStart = Math.Max(0, abilityOffset - 48);
        int windowEnd = Math.Max(windowStart, abilityOffset - 4);
        int? best = null;

        for (int offset = windowStart; offset <= windowEnd && offset <= data.Length - 4; offset += 4)
        {
            int value = BitConverter.ToInt32(data, offset);
            if (value is >= 1 and <= 30)
                best = value;
        }

        for (int offset = windowStart; offset <= windowEnd && offset <= data.Length - 2; offset += 2)
        {
            int value = BitConverter.ToInt16(data, offset);
            if (value is >= 1 and <= 30)
                best = value;
        }

        for (int offset = windowStart; offset <= windowEnd && offset < data.Length; offset++)
        {
            int value = data[offset];
            if (value is >= 1 and <= 30)
                best = value;
        }

        if (!best.HasValue)
            return false;

        level = best.Value;
        return true;
    }

    private static List<TokenSpan> ExtractAsciiTokenSpans(byte[] data, bool distinct)
    {
        var tokens = new List<TokenSpan>();
        var buffer = new StringBuilder();
        int currentStart = -1;
        int currentIndex = -1;

        void Flush()
        {
            string token = buffer.ToString().Trim();
            buffer.Clear();
            int endIndex = currentIndex;

            if (token.Length == 0)
            {
                currentStart = -1;
                return;
            }

            bool isNumericToken = token.All(char.IsDigit);
            bool isKnownShortKey = ShortKeyTokens.Contains(token);
            if (token.Length < 3)
            {
                if (!isNumericToken && !isKnownShortKey)
                {
                    currentStart = -1;
                    return;
                }
            }
            if (distinct && tokens.Any(t => string.Equals(t.Token, token, StringComparison.OrdinalIgnoreCase)))
            {
                currentStart = -1;
                return;
            }

            int safeStart = Math.Max(0, currentStart);
            int safeEnd = Math.Max(safeStart, endIndex);
            tokens.Add(new TokenSpan(token, safeStart, safeEnd));
            currentStart = -1;
        }

        for (int i = 0; i < data.Length; i++)
        {
            currentIndex = i;
            byte b = data[i];
            bool printable = b is >= 32 and <= 126;
            if (!printable)
            {
                Flush();
                continue;
            }

            char ch = (char)b;
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '.' or '/' or '&')
            {
                if (buffer.Length == 0)
                    currentStart = i;
                buffer.Append(ch);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return tokens;
    }

    private static List<string> ExtractAsciiTokens(byte[] data, bool distinct)
        => ExtractAsciiTokenSpans(data, distinct).Select(t => t.Token).ToList();
}
