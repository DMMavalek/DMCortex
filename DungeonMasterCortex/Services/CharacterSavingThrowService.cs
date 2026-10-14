using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class CharacterSavingThrowService
{
    public sealed record SaveResult(string Label, string CategoryKey, int BaseTarget, int Bonus, int FinalTarget);

    private sealed record SavingThrowCategory(string Key, string Label, string[] BonusKeys);

    private static readonly SavingThrowCategory[] Categories =
    {
        new("death_poison", "Paralyzation / Poison / Death Magic", new[] { "death", "poison" }),
        new("rod_staff_wand", "Rod / Staff / Wand", new[] { "rod_staff_wand" }),
        new("petrification_polymorph", "Petrification / Polymorph", new[] { "petrification_polymorph" }),
        new("breath_weapon", "Breath Weapon", new[] { "breath_weapon" }),
        new("spell", "Spell", new[] { "spell", "magic" }),
    };

    public static IReadOnlyList<SaveResult> Calculate(CharacterSheet character)
    {
        List<string> classIds = (character.ClassIds is { Count: > 0 }
                ? character.ClassIds
                : string.IsNullOrWhiteSpace(character.ClassId)
                    ? new List<string>()
                    : new List<string> { character.ClassId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (classIds.Count == 0)
            classIds.Add("fighter");

        return Categories.Select(category =>
        {
            int baseTarget = classIds
                .Select(classId => GetBaseSavingThrowTarget(classId, GetClassLevel(character, classId), category.Key))
                .Min();
            int bonus = GetBestApplicableSaveBonus(character.Bonuses, category.BonusKeys);
            return new SaveResult(category.Label, category.Key, baseTarget, bonus, Math.Clamp(baseTarget - bonus, 1, 30));
        }).ToList();
    }

    public static CharacterSavingThrows CalculateTargets(CharacterSheet character, CharacterSavingThrows? existing = null)
    {
        Dictionary<string, SaveResult> results = Calculate(character)
            .ToDictionary(result => result.CategoryKey, StringComparer.OrdinalIgnoreCase);
        return new CharacterSavingThrows
        {
            ParalyzationPoisonDeath = results["death_poison"].BaseTarget,
            RodStaffWand = results["rod_staff_wand"].BaseTarget,
            PetrificationPolymorph = results["petrification_polymorph"].BaseTarget,
            BreathWeapon = results["breath_weapon"].BaseTarget,
            Spell = results["spell"].BaseTarget,
            ExtensionData = existing?.ExtensionData ?? new Dictionary<string, System.Text.Json.JsonElement>(),
        };
    }

    private static int GetClassLevel(CharacterSheet character, string classId)
        => character.ClassLevels.TryGetValue(classId, out int classLevel) && classLevel > 0
            ? classLevel
            : Math.Max(1, character.Level);

    private static int GetBaseSavingThrowTarget(string classId, int level, string categoryKey)
    {
        string group = ResolveClassSaveGroup(classId);
        int band = SaveLevelBand(level, group);
        return group switch
        {
            "warrior" => categoryKey switch
            {
                "death_poison" => new[] { 14, 13, 11, 10, 8, 7, 5, 4, 3, 3 }[band],
                "rod_staff_wand" => new[] { 16, 15, 13, 12, 10, 9, 7, 6, 5, 5 }[band],
                "petrification_polymorph" => new[] { 15, 14, 12, 11, 9, 8, 6, 5, 4, 4 }[band],
                "breath_weapon" => new[] { 17, 16, 13, 12, 9, 8, 5, 4, 4, 4 }[band],
                _ => new[] { 17, 16, 14, 13, 11, 10, 8, 7, 6, 6 }[band],
            },
            "priest" => categoryKey switch
            {
                "death_poison" => new[] { 10, 9, 7, 6, 5, 4, 2 }[band],
                "rod_staff_wand" => new[] { 14, 13, 11, 10, 9, 8, 6 }[band],
                "petrification_polymorph" => new[] { 13, 12, 10, 9, 8, 7, 5 }[band],
                "breath_weapon" => new[] { 16, 15, 13, 12, 11, 10, 8 }[band],
                _ => new[] { 15, 14, 12, 11, 10, 9, 7 }[band],
            },
            "rogue" => categoryKey switch
            {
                "death_poison" => new[] { 13, 12, 11, 10, 9 }[band],
                "rod_staff_wand" => new[] { 14, 12, 10, 8, 6 }[band],
                "petrification_polymorph" => new[] { 12, 11, 10, 9, 8 }[band],
                "breath_weapon" => new[] { 16, 15, 14, 13, 12 }[band],
                _ => new[] { 15, 13, 11, 9, 7 }[band],
            },
            _ => categoryKey switch
            {
                "death_poison" => new[] { 14, 13, 11, 10 }[band],
                "rod_staff_wand" => new[] { 11, 9, 7, 5 }[band],
                "petrification_polymorph" => new[] { 13, 11, 9, 7 }[band],
                "breath_weapon" => new[] { 15, 13, 11, 9 }[band],
                _ => new[] { 12, 10, 8, 6 }[band],
            },
        };
    }

    private static int SaveLevelBand(int level, string group)
    {
        int clamped = Math.Clamp(level, 1, 20);
        return group switch
        {
            "warrior" => (clamped - 1) / 2,
            "priest" => (clamped - 1) / 3,
            "rogue" => (clamped - 1) / 4,
            _ => (clamped - 1) / 5,
        };
    }

    private static string ResolveClassSaveGroup(string classId)
        => (classId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fighter" or "paladin" or "ranger" => "warrior",
            "cleric" or "druid" => "priest",
            "thief" or "bard" or "rogue" => "rogue",
            _ => "wizard",
        };

    private static int GetBestApplicableSaveBonus(AbilityBonuses bonuses, IEnumerable<string> bonusKeys)
    {
        int allBonus = CombatModifierService.GetSavingThrowBonus(bonuses, "all");
        int best = bonusKeys
            .Select(key => CombatModifierService.GetSavingThrowBonus(bonuses, key))
            .DefaultIfEmpty(allBonus)
            .Max();
        return Math.Max(allBonus, best);
    }
}