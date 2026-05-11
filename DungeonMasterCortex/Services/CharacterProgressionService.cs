using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public sealed class LevelAdvancementResult
{
    public bool LeveledUp { get; set; }
    public int OldLevel { get; set; }
    public int NewLevel { get; set; }
    public int LevelsGained { get; set; }
    public int OldHitPoints { get; set; }
    public int NewHitPoints { get; set; }
    public int OldThac0 { get; set; }
    public int NewThac0 { get; set; }
    public int NextLevelXp { get; set; }
    public int CurrentXp { get; set; }
    public int ProficiencyChoicesGained { get; set; }
    public int RogueSkillPointsGained { get; set; }
    public string OldAttackRate { get; set; } = "1/round";
    public string NewAttackRate { get; set; } = "1/round";
}

public static class CharacterProgressionService
{
    private static readonly int[] FighterXp =
    {
        0, 2000, 4000, 8000, 16000, 32000, 64000, 125000, 250000, 500000,
        750000, 1000000, 1250000, 1500000, 1750000, 2000000, 2250000, 2500000, 2750000, 3000000,
    };

    private static readonly int[] PaladinRangerXp =
    {
        0, 2250, 4500, 9000, 18000, 36000, 75000, 150000, 300000, 600000,
        900000, 1200000, 1500000, 1800000, 2100000, 2400000, 2700000, 3000000, 3300000, 3600000,
    };

    private static readonly int[] RogueXp =
    {
        0, 1250, 2500, 5000, 10000, 20000, 40000, 70000, 110000, 160000,
        220000, 440000, 660000, 880000, 1100000, 1320000, 1540000, 1760000, 1980000, 2200000,
    };

    private static readonly int[] ClericXp =
    {
        0, 1500, 3000, 6000, 13000, 27500, 55000, 110000, 225000, 450000,
        675000, 900000, 1125000, 1350000, 1575000, 1800000, 2025000, 2250000, 2475000, 2700000,
    };

    private static readonly int[] DruidXp =
    {
        0, 2000, 4000, 7500, 12500, 20000, 35000, 60000, 90000, 125000,
        200000, 300000, 750000, 1500000, 3000000, 3500000, 4000000, 4500000, 5000000, 5500000,
    };

    private static readonly int[] WizardXp =
    {
        0, 2500, 5000, 10000, 20000, 40000, 60000, 90000, 135000, 250000,
        375000, 750000, 1125000, 1500000, 1875000, 2250000, 2625000, 3000000, 3375000, 3750000,
    };

    private static readonly Dictionary<int, int[]> PriestSpellSlotsByLevel = new()
    {
        [1] = new[] { 1, 0, 0, 0, 0, 0, 0 },
        [2] = new[] { 2, 0, 0, 0, 0, 0, 0 },
        [3] = new[] { 2, 1, 0, 0, 0, 0, 0 },
        [4] = new[] { 3, 2, 0, 0, 0, 0, 0 },
        [5] = new[] { 3, 3, 1, 0, 0, 0, 0 },
        [6] = new[] { 3, 3, 2, 0, 0, 0, 0 },
        [7] = new[] { 3, 3, 2, 1, 0, 0, 0 },
        [8] = new[] { 3, 3, 3, 2, 0, 0, 0 },
        [9] = new[] { 4, 4, 3, 2, 1, 0, 0 },
        [10] = new[] { 4, 4, 3, 3, 2, 0, 0 },
        [11] = new[] { 5, 4, 4, 3, 2, 1, 0 },
        [12] = new[] { 6, 5, 5, 3, 2, 2, 0 },
        [13] = new[] { 6, 6, 6, 4, 2, 2, 0 },
        [14] = new[] { 6, 6, 6, 5, 3, 2, 1 },
        [15] = new[] { 6, 6, 6, 6, 4, 2, 1 },
        [16] = new[] { 7, 7, 7, 6, 4, 3, 1 },
        [17] = new[] { 7, 7, 7, 7, 5, 3, 2 },
        [18] = new[] { 8, 8, 8, 8, 6, 4, 2 },
        [19] = new[] { 9, 9, 8, 8, 6, 4, 2 },
        [20] = new[] { 9, 9, 9, 8, 7, 5, 2 },
    };

    private static readonly Dictionary<int, int[]> WizardSpellSlotsByLevel = new()
    {
        [1] = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 },
        [2] = new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 },
        [3] = new[] { 2, 1, 0, 0, 0, 0, 0, 0, 0 },
        [4] = new[] { 3, 2, 0, 0, 0, 0, 0, 0, 0 },
        [5] = new[] { 4, 2, 1, 0, 0, 0, 0, 0, 0 },
        [6] = new[] { 4, 2, 2, 0, 0, 0, 0, 0, 0 },
        [7] = new[] { 4, 3, 2, 1, 0, 0, 0, 0, 0 },
        [8] = new[] { 4, 3, 3, 2, 0, 0, 0, 0, 0 },
        [9] = new[] { 4, 3, 3, 2, 1, 0, 0, 0, 0 },
        [10] = new[] { 4, 4, 3, 2, 2, 0, 0, 0, 0 },
        [11] = new[] { 4, 4, 4, 3, 3, 0, 0, 0, 0 },
        [12] = new[] { 4, 4, 4, 4, 4, 1, 0, 0, 0 },
        [13] = new[] { 5, 5, 5, 4, 4, 2, 0, 0, 0 },
        [14] = new[] { 5, 5, 5, 4, 4, 2, 1, 0, 0 },
        [15] = new[] { 5, 5, 5, 5, 5, 2, 1, 0, 0 },
        [16] = new[] { 5, 5, 5, 5, 5, 3, 2, 1, 0 },
        [17] = new[] { 5, 5, 5, 5, 5, 3, 3, 2, 0 },
        [18] = new[] { 5, 5, 5, 5, 5, 3, 3, 2, 1 },
        [19] = new[] { 5, 5, 5, 5, 5, 3, 3, 3, 1 },
        [20] = new[] { 5, 5, 5, 5, 5, 4, 3, 3, 2 },
    };

    private static readonly Dictionary<int, int[]> PaladinSpellSlotsByLevel = new()
    {
        [4] = new[] { 1, 0, 0, 0 },
        [5] = new[] { 1, 0, 0, 0 },
        [6] = new[] { 2, 0, 0, 0 },
        [7] = new[] { 2, 1, 0, 0 },
        [8] = new[] { 2, 1, 0, 0 },
        [9] = new[] { 2, 2, 0, 0 },
        [10] = new[] { 2, 2, 1, 0 },
        [11] = new[] { 2, 2, 2, 0 },
        [12] = new[] { 3, 2, 2, 0 },
        [13] = new[] { 3, 2, 2, 0 },
        [14] = new[] { 3, 2, 2, 1 },
        [15] = new[] { 3, 3, 2, 1 },
        [16] = new[] { 3, 3, 3, 1 },
        [17] = new[] { 3, 3, 3, 1 },
        [18] = new[] { 3, 3, 3, 2 },
        [19] = new[] { 3, 3, 3, 3 },
        [20] = new[] { 4, 3, 3, 3 },
    };

    private static readonly Dictionary<int, int[]> RangerSpellSlotsByLevel = new()
    {
        [8] = new[] { 1, 0, 0 },
        [9] = new[] { 2, 0, 0 },
        [10] = new[] { 2, 1, 0 },
        [11] = new[] { 2, 2, 0 },
        [12] = new[] { 2, 2, 1 },
        [13] = new[] { 3, 2, 1 },
        [14] = new[] { 3, 2, 2 },
        [15] = new[] { 3, 3, 2 },
        [16] = new[] { 3, 3, 3 },
    };

    public static void InitializeCharacterProgression(CharacterSheet character, bool seedLevelRewards)
    {
        if (character is null)
            throw new ArgumentNullException(nameof(character));

        var classToken = ResolvePrimaryClassToken(character.ClassId);
        int clampedLevel = Math.Clamp(character.Level, 1, 20);
        character.Level = clampedLevel;

        int minXpForCurrentLevel = GetMinimumExperienceForLevel(classToken, clampedLevel);
        if (character.ExperiencePoints < minXpForCurrentLevel)
            character.ExperiencePoints = minXpForCurrentLevel;

        character.Thac0 = GetBaseThac0(classToken, clampedLevel) - character.Bonuses.AttackBonus;
        character.AttackRate = GetWarriorAttackRate(classToken, clampedLevel);

        ApplySpellSlotProgression(character, classToken, clampedLevel);

        if (seedLevelRewards)
        {
            character.UnspentProficiencyChoices += Math.Max(0, clampedLevel - 1);
            if (IsRogueClass(classToken))
                character.UnspentRogueSkillPoints += Math.Max(0, clampedLevel - 1) * RulesEngine.GetRogueSkillPerLevelGain();
        }
    }

    public static LevelAdvancementResult ApplyLevelAdvancementFromExperience(CharacterSheet character, bool applyHitPointProgression = true)
    {
        if (character is null)
            throw new ArgumentNullException(nameof(character));

        InitializeCharacterProgression(character, seedLevelRewards: false);

        var classToken = ResolvePrimaryClassToken(character.ClassId);
        int oldLevel = Math.Clamp(character.Level, 1, 20);
        int targetLevel = GetLevelForExperience(classToken, character.ExperiencePoints);

        var result = new LevelAdvancementResult
        {
            OldLevel = oldLevel,
            NewLevel = oldLevel,
            LevelsGained = 0,
            OldHitPoints = character.HitPoints,
            NewHitPoints = character.HitPoints,
            OldThac0 = character.Thac0,
            NewThac0 = character.Thac0,
            CurrentXp = character.ExperiencePoints,
            NextLevelXp = GetMinimumExperienceForLevel(classToken, Math.Min(20, oldLevel + 1)),
            OldAttackRate = character.AttackRate,
            NewAttackRate = character.AttackRate,
        };

        if (targetLevel <= oldLevel)
            return result;

        if (applyHitPointProgression)
        {
            for (int level = oldLevel + 1; level <= targetLevel; level++)
            {
                int hpGain = CalculateHitPointGainForLevel(character, classToken, level);
                character.HitPoints += hpGain;
                RecordHitPointGainAtLevel(character, level, hpGain);
            }
        }

        int levelsGained = targetLevel - oldLevel;
        character.Level = targetLevel;
        character.Thac0 = GetBaseThac0(classToken, targetLevel) - character.Bonuses.AttackBonus;
        character.AttackRate = GetWarriorAttackRate(classToken, targetLevel);
        ApplySpellSlotProgression(character, classToken, targetLevel);

        int proficiencyGain = levelsGained;
        int rogueGain = IsRogueClass(classToken) ? levelsGained * RulesEngine.GetRogueSkillPerLevelGain() : 0;

        character.UnspentProficiencyChoices += proficiencyGain;
        character.UnspentRogueSkillPoints += rogueGain;

        result.LeveledUp = true;
        result.NewLevel = targetLevel;
        result.LevelsGained = levelsGained;
        result.NewHitPoints = character.HitPoints;
        result.NewThac0 = character.Thac0;
        result.ProficiencyChoicesGained = proficiencyGain;
        result.RogueSkillPointsGained = rogueGain;
        result.NewAttackRate = character.AttackRate;
        result.NextLevelXp = targetLevel >= 20
            ? GetMinimumExperienceForLevel(classToken, 20)
            : GetMinimumExperienceForLevel(classToken, targetLevel + 1);

        return result;
    }

    public static void RecordHitPointGainAtLevel(CharacterSheet character, int level, int hitPointGain)
    {
        if (character is null)
            throw new ArgumentNullException(nameof(character));
        if (level < 1 || hitPointGain <= 0)
            return;

        int existing = character.HitPointGainByLevel.GetValueOrDefault(level, 0);
        character.HitPointGainByLevel[level] = existing + hitPointGain;
    }

    public static void RecordHitPointGainAcrossLevels(CharacterSheet character, int oldLevel, int newLevel, int totalHitPointGain)
    {
        if (character is null)
            throw new ArgumentNullException(nameof(character));
        if (newLevel <= oldLevel || totalHitPointGain <= 0)
            return;

        int levelsGained = newLevel - oldLevel;
        int baseGain = totalHitPointGain / levelsGained;
        int remainder = totalHitPointGain % levelsGained;

        for (int i = 1; i <= levelsGained; i++)
        {
            int level = oldLevel + i;
            int gain = baseGain + (i <= remainder ? 1 : 0);
            RecordHitPointGainAtLevel(character, level, gain);
        }
    }

    /// <summary>
    /// Returns the hit die size and whether the given level is still within
    /// the hit-die range (vs. the flat post-cap gain) for the character's class.
    /// For multiclass characters the dice are averaged.
    /// </summary>
    public static (int HitDie, bool WithinDieRange) GetHitDieInfo(CharacterSheet character, int level)
    {
        var classIds = character.ClassMode == "multiclass" && character.ClassIds.Count > 0
            ? character.ClassIds
            : new List<string> { character.ClassId };

        int totalDie = 0;
        bool withinRange = false;
        foreach (var id in classIds)
        {
            var p = GetHitPointProfile(ResolvePrimaryClassToken(id));
            totalDie += p.HitDie;
            if (level <= p.HitDieLevels) withinRange = true;
        }
        return (Math.Max(1, totalDie / classIds.Count), withinRange);
    }

    /// <summary>
    /// Returns the CON modifier + class/race hp-per-level bonus for a character.
    /// Used to let the UI offer "add bonuses?" after a manual HP roll entry.
    /// </summary>
    public static int GetConAndClassHpBonus(CharacterSheet character)
    {
        int conMod = GetConHitPointBonus(character.Abilities.GetValueOrDefault("con", 10), character.ClassId);
        return conMod + character.Bonuses.HpPerLevel;
    }

    public static int GetConHitPointBonus(int con, string classId)
    {
        bool warriorBonus = ResolvePrimaryClassToken(classId) is "fighter" or "paladin" or "ranger" or "warrior";
        if (warriorBonus)
        {
            return con switch
            {
                <= 1 => -3,
                <= 3 => -2,
                <= 6 => -1,
                <= 14 => 0,
                15 => 1,
                16 => 2,
                17 => 3,
                18 => 4,
                19 => 5,
                _ => 5,
            };
        }

        return GetConModifier(con);
    }

    /// <summary>
    /// Rolls HP for the next level for the character (random die roll + bonuses).
    /// For multiclass characters averages the dice. Clamps to minimum 1.
    /// </summary>
    public static int RollHpForLevel(CharacterSheet character, int level)
    {
        var classIds = character.ClassMode == "multiclass" && character.ClassIds.Count > 0
            ? character.ClassIds
            : new List<string> { character.ClassId };

        int totalRoll = 0;
        foreach (var id in classIds)
        {
            var p = GetHitPointProfile(ResolvePrimaryClassToken(id));
            if (level <= p.HitDieLevels)
                totalRoll += Random.Shared.Next(1, p.HitDie + 1);
            else
                totalRoll += p.PostCapGain;
        }
        int baseRoll = classIds.Count > 1 ? (int)Math.Round((double)totalRoll / classIds.Count) : totalRoll;
        int bonus = GetConAndClassHpBonus(character);
        return Math.Max(1, baseRoll + bonus);
    }

    public static int GetMinimumExperienceForLevel(string classId, int level)
    {
        var table = GetExperienceTableForClass(classId);
        int clampedLevel = Math.Clamp(level, 1, table.Length);
        return table[clampedLevel - 1];
    }

    public static string GetPrimeRequisiteAbilityKey(string classId)
    {
        return ResolvePrimaryClassToken(classId) switch
        {
            "fighter" or "paladin" or "ranger" => "str",
            "thief" or "bard" or "rogue" => "dex",
            "cleric" or "druid" => "wis",
            "wizard" or "mage" or "illusionist" => "int",
            _ => "str",
        };
    }

    public static int GetPrimeRequisiteBonusPercent(CharacterSheet character)
    {
        if (character is null)
            throw new ArgumentNullException(nameof(character));

        string key = GetPrimeRequisiteAbilityKey(character.ClassId);
        int score = character.Abilities.GetValueOrDefault(key, 10);
        return score switch
        {
            >= 16 => 10,
            >= 13 => 5,
            _ => 0,
        };
    }

    public static int GetTotalExperienceBonusPercent(CharacterSheet character)
    {
        if (character is null)
            throw new ArgumentNullException(nameof(character));

        int prime = GetPrimeRequisiteBonusPercent(character);
        int racialClass = character.Bonuses?.XpModifierPercent ?? 0;
        return prime + racialClass;
    }

    public static int ApplyExperienceBonus(int baseExperienceGain, int totalBonusPercent)
    {
        int nonNegative = Math.Max(0, baseExperienceGain);
        if (nonNegative == 0 || totalBonusPercent == 0)
            return nonNegative;

        decimal multiplier = 1m + (totalBonusPercent / 100m);
        decimal adjusted = nonNegative * multiplier;
        // Standard midpoint rounding: .5 rounds away from zero.
        return (int)Math.Round(adjusted, MidpointRounding.AwayFromZero);
    }

    public static int GetLevelForExperience(string classId, int experiencePoints)
    {
        var table = GetExperienceTableForClass(classId);
        int xp = Math.Max(0, experiencePoints);
        int level = 1;

        for (int i = 0; i < table.Length; i++)
        {
            if (xp >= table[i])
                level = i + 1;
        }

        return Math.Min(20, level);
    }

    private static int[] GetExperienceTableForClass(string classId)
    {
        return ResolvePrimaryClassToken(classId) switch
        {
            "fighter" => FighterXp,
            "paladin" or "ranger" => PaladinRangerXp,
            "thief" or "bard" or "rogue" => RogueXp,
            "cleric" => ClericXp,
            "druid" => DruidXp,
            "wizard" or "mage" or "illusionist" => WizardXp,
            _ => FighterXp,
        };
    }

    private static int CalculateHitPointGainForLevel(CharacterSheet character, string classToken, int level)
    {
        var profile = GetHitPointProfile(classToken);
        int conMod = GetConHitPointBonus(character.Abilities.GetValueOrDefault("con", 10), classToken);
        int baseGain = level <= profile.HitDieLevels ? ((profile.HitDie + 1) / 2) : profile.PostCapGain;

        int total = baseGain + conMod + character.Bonuses.HpPerLevel;
        return Math.Max(1, total);
    }

    private static (int HitDie, int HitDieLevels, int PostCapGain) GetHitPointProfile(string classId)
    {
        return classId switch
        {
            "fighter" or "paladin" or "ranger" => (10, 9, 3),
            "thief" or "bard" or "rogue" => (6, 10, 2),
            "cleric" or "druid" => (8, 9, 2),
            "wizard" or "mage" or "illusionist" => (4, 10, 1),
            _ => (6, 10, 2),
        };
    }

    private static int GetConModifier(int con) => con switch
    {
        <= 6 => -2,
        <= 8 => -1,
        <= 14 => 0,
        <= 16 => 1,
        _ => 2,
    };

    private static int GetBaseThac0(string classId, int level)
    {
        int clampedLevel = Math.Max(1, level);
        return classId switch
        {
            "fighter" or "paladin" or "ranger" => 20 - (clampedLevel - 1),
            "cleric" or "druid" or "thief" or "bard" or "rogue" => 20 - ((clampedLevel - 1) / 2),
            "wizard" or "mage" or "illusionist" => 20 - ((clampedLevel - 1) / 3),
            _ => 20 - ((clampedLevel - 1) / 2),
        };
    }

    private static string GetWarriorAttackRate(string classId, int level)
    {
        if (!IsWarriorClass(classId))
            return "1/round";

        if (level >= 13) return "2/round";
        if (level >= 7) return "3/2 rounds";
        return "1/round";
    }

    private static void ApplySpellSlotProgression(CharacterSheet character, string classId, int level)
    {
        character.DivineSpellSlots = BuildSlotDictionary(GetDivineSlots(classId, level));
        character.ArcaneSpellSlots = BuildSlotDictionary(GetArcaneSlots(classId, level));
    }

    private static int[] GetDivineSlots(string classId, int level)
    {
        if (classId is "cleric" or "druid")
            return GetTableRow(PriestSpellSlotsByLevel, level, 7);

        if (classId == "paladin")
            return GetTableRow(PaladinSpellSlotsByLevel, level, 4);

        if (classId == "ranger")
            return GetTableRow(RangerSpellSlotsByLevel, level, 3);

        return Array.Empty<int>();
    }

    private static int[] GetArcaneSlots(string classId, int level)
    {
        if (classId is "wizard" or "mage" or "illusionist")
            return GetTableRow(WizardSpellSlotsByLevel, level, 9);

        return Array.Empty<int>();
    }

    private static int[] GetTableRow(Dictionary<int, int[]> table, int level, int width)
    {
        int key = Math.Clamp(level, 1, 20);
        while (key >= 1)
        {
            if (table.TryGetValue(key, out var row))
                return row;
            key--;
        }

        return Enumerable.Repeat(0, width).ToArray();
    }

    private static Dictionary<int, int> BuildSlotDictionary(int[] slots)
    {
        var result = new Dictionary<int, int>();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] > 0)
                result[i + 1] = slots[i];
        }

        return result;
    }

    private static bool IsWarriorClass(string classId)
        => classId is "fighter" or "paladin" or "ranger";

    private static bool IsRogueClass(string classId)
        => classId is "thief" or "bard" or "rogue";

    private static string ResolvePrimaryClassToken(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return "fighter";

        var parts = classId
            .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var raw in parts)
        {
            string token = raw.Trim().ToLowerInvariant();
            string mapped = token switch
            {
                "warrior" => "fighter",
                "priest" => "cleric",
                "rogue" => "thief",
                _ => token,
            };

            if (mapped is "fighter" or "paladin" or "ranger" or "thief" or "bard" or "cleric" or "druid" or "wizard" or "mage" or "illusionist")
                return mapped;
        }

        return "fighter";
    }
}
