using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class CharacterArmorService
{
    public static void RecalculateArmorForCharacter(CharacterSheet character, IReadOnlyList<CustomEquipmentData> library)
    {
        if (character is null)
            return;

        var byId = BuildLibraryLookup(library);

        // Resolve body armor: use explicit EquippedArmorId slot; fall back to best in inventory.
        CustomEquipmentData? equippedArmor = ResolveEquippedArmor(
            character.EquippedArmorId, character.EquipmentSelections, byId);

        // Resolve shield: use explicit EquippedShieldId slot; fall back to first shield in inventory.
        CustomEquipmentData? equippedShield = ResolveEquippedShield(
            character.EquippedShieldId, character.EquipmentSelections, byId);

        bool isUnarmored = equippedArmor is null;
        int acBonus = CalculateCurrentAcBonus(character, isUnarmored, equippedArmor);

        int armorAdjustment = equippedArmor is null ? 0 : equippedArmor.ArmorClassValue - 10;
        int shieldBonus    = equippedShield?.ArmorClassValue ?? 0;   // typically -1

        character.BaseArmorClass = 10;
        character.ArmorClass = 10 + acBonus + armorAdjustment + shieldBonus;
        character.ArmorProfile = equippedArmor?.RogueArmorProfile ?? "no_armor";

        if (IsRogueClass(character.ClassId))
            character.RogueSkillArmorProfile = equippedArmor?.RogueArmorProfile ?? "no_armor";
    }

    private static int CalculateCurrentAcBonus(CharacterSheet character, bool isUnarmored, CustomEquipmentData? equippedArmor)
    {
        var unlockedStructuredAbilities = character.StructuredAbilities
            .Where(a => RulesEngine.AbilityUnlockLevel(a) <= Math.Max(1, character.Level))
            .ToList();

        if (unlockedStructuredAbilities.Count == 0)
            return character.Bonuses?.AcBonus ?? GetDexterityAcAdjustment(character.Abilities.GetValueOrDefault("dex", 10));

        var currentBonuses = RulesEngine.AggregateEffects(unlockedStructuredAbilities, isUnarmored);
        var subTotals = SubAbilityTables.CalculateTotals(character.SubAbilities, character.ExceptionalStrength, character.ClassId);

        int acBonus = currentBonuses.AcBonus + subTotals.ArmorClassAdjustment;

        // Tough hide grants natural AC 8 while unarmored and a smaller benefit in very light armor.
        if (unlockedStructuredAbilities.Any(a => string.Equals(a.Id, "human_tough_hide", StringComparison.OrdinalIgnoreCase))
            && equippedArmor is not null
            && equippedArmor.ArmorClassValue >= 8)
        {
            acBonus -= 1;
        }

        return acBonus;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Dictionary<string, CustomEquipmentData> BuildLibraryLookup(IReadOnlyList<CustomEquipmentData> library)
        => library
            .GroupBy(x => EquipmentLibraryService.CanonicalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    private static CustomEquipmentData? ResolveEquippedArmor(
        string equippedId,
        IEnumerable<EquipmentSelection> selections,
        Dictionary<string, CustomEquipmentData> byId)
    {
        // If an explicit armor slot is set, use it.
        if (!string.IsNullOrWhiteSpace(equippedId))
        {
            string key = EquipmentLibraryService.CanonicalizeId(equippedId);
            bool inInventory = (selections ?? Enumerable.Empty<EquipmentSelection>())
                .Any(s => s.Quantity > 0
                          && string.Equals(EquipmentLibraryService.CanonicalizeId(s.ItemId), key, StringComparison.OrdinalIgnoreCase));
            if (inInventory && byId.TryGetValue(key, out var item) && item.IsArmor)
                return item;
        }

        // Fall back: pick the best non-shield armor in the inventory.
        var candidates = new List<CustomEquipmentData>();
        foreach (var sel in selections ?? Enumerable.Empty<EquipmentSelection>())
        {
            if (sel.Quantity <= 0) continue;
            string key = EquipmentLibraryService.CanonicalizeId(sel.ItemId);
            if (byId.TryGetValue(key, out var item) && item.IsArmor && !item.IsShield)
                candidates.Add(item);
        }
        return candidates.OrderBy(x => x.ArmorClassValue).FirstOrDefault();
    }

    private static CustomEquipmentData? ResolveEquippedShield(
        string equippedId,
        IEnumerable<EquipmentSelection> selections,
        Dictionary<string, CustomEquipmentData> byId)
    {
        // If an explicit shield slot is set, use it.
        if (!string.IsNullOrWhiteSpace(equippedId))
        {
            string key = EquipmentLibraryService.CanonicalizeId(equippedId);
            bool inInventory = (selections ?? Enumerable.Empty<EquipmentSelection>())
                .Any(s => s.Quantity > 0
                          && string.Equals(EquipmentLibraryService.CanonicalizeId(s.ItemId), key, StringComparison.OrdinalIgnoreCase));
            if (inInventory && byId.TryGetValue(key, out var item) && item.IsShield)
                return item;
        }

        // No auto-equip fallback for shields — user must explicitly equip one.
        return null;
    }

    private static bool IsRogueClass(string? classId)
        => string.Equals(classId, "thief", StringComparison.OrdinalIgnoreCase)
           || string.Equals(classId, "bard", StringComparison.OrdinalIgnoreCase);

    private static int GetDexterityAcAdjustment(int dexterity)
        => dexterity switch
        {
            <= 3 => 3,
            <= 5 => 2,
            <= 7 => 1,
            <= 14 => 0,
            15 => -1,
            16 => -2,
            17 => -3,
            _ => -4,
        };
}
