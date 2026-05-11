using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class CombatModifierService
{
    private static readonly Dictionary<string, string[]> EnemyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["goblinoids"] = new[] { "goblinoid", "goblin", "hobgoblin", "bugbear", "kobold", "orc" },
        ["giants"] = new[] { "giant", "ogre", "troll", "ettin", "hill giant", "stone giant", "frost giant", "fire giant", "cloud giant", "storm giant" },
        ["gnolls"] = new[] { "gnoll" },
        ["goblins"] = new[] { "goblin" },
        ["kobolds"] = new[] { "kobold" },
        ["drow"] = new[] { "drow", "dark elf" },
        ["all"] = Array.Empty<string>(),
    };

    private static readonly Dictionary<string, string[]> SaveAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spell"] = new[] { "spell", "spells", "magic", "magical" },
        ["magic"] = new[] { "magic", "magical", "spell", "spells" },
        ["sleep"] = new[] { "sleep", "slumber" },
        ["charm"] = new[] { "charm", "charming", "enchantment" },
        ["poison"] = new[] { "poison", "poisons", "venom" },
        ["cold"] = new[] { "cold", "ice", "frost" },
        ["fire"] = new[] { "fire", "flame", "heat" },
        ["electricity"] = new[] { "electricity", "electric", "lightning", "shock" },
        ["sound"] = new[] { "sound", "sonic", "thunder" },
        ["energy_drain"] = new[] { "energy_drain", "level_drain", "drain" },
        ["death"] = new[] { "death", "death_magic" },
        ["rod_staff_wand"] = new[] { "rod", "staff", "wand", "rods", "staves" },
        ["petrification_polymorph"] = new[] { "petrification", "polymorph", "petrify" },
        ["breath_weapon"] = new[] { "breath", "breath_weapon", "dragon breath" },
    };

    public static int GetSavingThrowBonus(AbilityBonuses? bonuses, string saveCategory)
    {
        if (bonuses is null || bonuses.SaveBonuses.Count == 0)
            return 0;

        string key = NormalizeKey(saveCategory);
        int total = 0;
        var keysToApply = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "all" };

        if (!string.IsNullOrWhiteSpace(key))
            keysToApply.Add(key);

        foreach (var (aliasKey, synonyms) in SaveAliases)
        {
            if (key.Equals(aliasKey, StringComparison.OrdinalIgnoreCase)
                || synonyms.Any(s => key.Equals(NormalizeKey(s), StringComparison.OrdinalIgnoreCase)))
            {
                keysToApply.Add(aliasKey);
            }
        }

        foreach (string applyKey in keysToApply)
            total += bonuses.SaveBonuses.GetValueOrDefault(applyKey);

        return total;
    }

    public static int GetAdjustedSavingThrowTarget(int baseTarget, AbilityBonuses? bonuses, string saveCategory)
    {
        int bonus = GetSavingThrowBonus(bonuses, saveCategory);
        // Descending save target in AD&D: lower target is better, so subtract positive bonuses.
        return Math.Clamp(baseTarget - bonus, 1, 30);
    }

    public static bool IsSavingThrowSuccessful(int d20Roll, int adjustedTarget)
        => d20Roll >= adjustedTarget;

    public static int GetEnemyAttackBonus(AbilityBonuses? bonuses, string? enemyContext)
        => GetEnemyBonus(bonuses?.EnemyAttackBonuses, enemyContext);

    public static int GetEnemyDamageBonus(AbilityBonuses? bonuses, string? enemyContext)
        => GetEnemyBonus(bonuses?.EnemyDamageBonuses, enemyContext);

    private static int GetEnemyBonus(Dictionary<string, int>? bonusMap, string? enemyContext)
    {
        if (bonusMap is null || bonusMap.Count == 0)
            return 0;

        string context = NormalizeKey(enemyContext);
        int total = bonusMap.GetValueOrDefault("all");
        if (string.IsNullOrWhiteSpace(context))
            return total;

        foreach (var (key, value) in bonusMap)
        {
            string normalizedKey = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey == "all")
                continue;

            if (context.Contains(normalizedKey, StringComparison.OrdinalIgnoreCase))
            {
                total += value;
                continue;
            }

            if (EnemyAliases.TryGetValue(normalizedKey, out var aliases)
                && aliases.Any(alias => context.Contains(NormalizeKey(alias), StringComparison.OrdinalIgnoreCase)))
            {
                total += value;
            }
        }

        return total;
    }

    private static string NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return raw.Trim().ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_');
    }
}