using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class CharacterArmorService
{
    private static readonly Regex SetAcRegex = new(
        @"\bset(?:s)?\s+ac\s+to\s+(?<ac>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VariantAcRegex = new(
        @"\bac\s*(?<ac>\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void RecalculateArmorForCharacter(CharacterSheet character, IReadOnlyList<CustomEquipmentData> library)
    {
        if (character is null)
            return;

        var byId = BuildLibraryLookup(library);
        var byName = BuildLibraryNameLookup(library);

        // Resolve body armor: use explicit EquippedArmorId slot; fall back to best in inventory.
        CustomEquipmentData? equippedArmor = ResolveEquippedArmor(
            character.EquippedArmorId, character.EquipmentSelections, byId);

        // Resolve shield: use explicit EquippedShieldId slot; fall back to first shield in inventory.
        CustomEquipmentData? equippedShield = ResolveEquippedShield(
            character.EquippedShieldId, character.EquipmentSelections, byId);

        bool isUnarmored = equippedArmor is null;
        int acBonus = CalculateCurrentAcBonus(character, isUnarmored, equippedArmor);

        int equippedArmorAc = equippedArmor is null ? 10 : DescribeArmorClassSource(equippedArmor, byName).EffectiveArmorClass;
        int armorAdjustment = equippedArmor is null ? 0 : equippedArmorAc - 10;

        bool hasShieldProf = HasShieldProficiency(character);
        var (shieldMeleeBonus, shieldMissileBonus) = equippedShield is null
            ? (0, 0)
            : GetShieldBonus(equippedShield, hasShieldProf, byName);

        int magicalWearableAcAdjustment = GetEquippedMagicalWearableAcAdjustment(character, byId);

        character.BaseArmorClass = 10;
        character.ArmorClass = 10 + acBonus + armorAdjustment + shieldMeleeBonus + magicalWearableAcAdjustment;
        character.MissileArmorClass = 10 + acBonus + armorAdjustment + shieldMissileBonus + magicalWearableAcAdjustment;

        string armorName = (equippedArmor?.Name ?? string.Empty).ToLowerInvariant();
        string profile = "no_armor";
        if (equippedArmor is not null)
        {
            if (armorName.Contains("full plate")) profile = "full_plate";
            else if (armorName.Contains("field plate")) profile = "field_plate";
            else if (armorName.Contains("plate mail")) profile = "plate_mail";
            else if (armorName.Contains("bronze plate")) profile = "bronze_plate_mail";
            else if (armorName.Contains("banded")) profile = "banded_mail";
            else if (armorName.Contains("splint")) profile = "splint_mail";
            else if (armorName.Contains("chain")) profile = "chain_mail";
            else if (armorName.Contains("scale")) profile = "scale_mail";
            else if (armorName.Contains("ring")) profile = "ring_mail";
            else if (armorName.Contains("brigandine")) profile = "brigandine";
            else if (armorName.Contains("hide")) profile = "hide";
            else if (armorName.Contains("studded")) profile = "studded_leather";
            else if (armorName.Contains("leather")) profile = "leather";
            else if (armorName.Contains("padded")) profile = "padded";
            else profile = equippedArmor.RogueArmorProfile ?? "no_armor";
        }
        character.ArmorProfile = profile;

        if (IsRogueClass(character.ClassId))
        {
            character.RogueSkillArmorProfile = profile is "full_plate" or "field_plate" or "plate_mail" or "bronze_plate_mail" or "banded_mail" or "splint_mail"
                ? "heavy_armor"
                : (equippedArmor?.RogueArmorProfile ?? "no_armor");
        }
    }

    private static bool HasShieldProficiency(CharacterSheet character)
    {
        if (character is null) return false;
        bool inWp = character.WeaponProficiencies.Any(wp =>
            string.Equals(wp.ProficiencyId, "shield_proficiency", StringComparison.OrdinalIgnoreCase)
            || wp.DisplayName.Contains("Shield Proficiency", StringComparison.OrdinalIgnoreCase));
        if (inWp) return true;
        return character.SelectedClassAbilityIds.Any(ca =>
            ca.Contains("shield_proficiency", StringComparison.OrdinalIgnoreCase));
    }

    private static (int MeleeBonus, int MissileBonus) GetShieldBonus(
        CustomEquipmentData shield,
        bool hasProficiency,
        IReadOnlyDictionary<string, CustomEquipmentData>? libraryByName)
    {
        string name = (shield.Name ?? string.Empty).ToLowerInvariant();
        string id = (shield.Id ?? string.Empty).ToLowerInvariant();

        int magicalAdjustment = 0;
        if (shield.EnchantmentBonus != 0 || shield.ArmorClassModifier != 0)
        {
            magicalAdjustment = shield.ArmorClassModifier != 0
                ? shield.ArmorClassModifier
                : -shield.EnchantmentBonus;
        }

        int meleeBase;
        int missileBase;

        if (name.Contains("buckler") || id.Contains("buckler"))
        {
            meleeBase = -1;
            missileBase = -1;
        }
        else if (name.Contains("body") || id.Contains("body") || name.Contains("tower") || id.Contains("tower"))
        {
            meleeBase = hasProficiency ? -3 : -1;
            missileBase = hasProficiency ? -4 : -2;
        }
        else if (name.Contains("small") || id.Contains("small"))
        {
            meleeBase = hasProficiency ? -2 : -1;
            missileBase = hasProficiency ? -2 : -1;
        }
        else
        {
            // Standard / Medium shield
            meleeBase = hasProficiency ? -3 : -1;
            missileBase = hasProficiency ? -3 : -1;
        }

        return (meleeBase + magicalAdjustment, missileBase + magicalAdjustment);
    }

    private static int CalculateCurrentAcBonus(CharacterSheet character, bool isUnarmored, CustomEquipmentData? equippedArmor)
    {
        var unlockedStructuredAbilities = character.StructuredAbilities
            .Where(a => RulesEngine.AbilityUnlockLevel(a) <= Math.Max(1, character.Level))
            .ToList();

        if (unlockedStructuredAbilities.Count == 0)
            return character.Bonuses?.AcBonus ?? GetDexterityAcAdjustment(character.Abilities.GetValueOrDefault("dex", 10));

        var currentBonuses = RulesEngine.AggregateEffects(unlockedStructuredAbilities, isUnarmored, Math.Max(1, character.Level));
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

    private static Dictionary<string, CustomEquipmentData> BuildLibraryNameLookup(IReadOnlyList<CustomEquipmentData> library)
        => library
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => (x.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
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
            if (inInventory && byId.TryGetValue(key, out var item) && item.IsArmor && !item.IsShield && !item.IsWeapon)
                return item;
        }

        // Fall back: pick the best non-shield armor in the inventory.
        var candidates = new List<CustomEquipmentData>();
        foreach (var sel in selections ?? Enumerable.Empty<EquipmentSelection>())
        {
            if (sel.Quantity <= 0) continue;
            string key = EquipmentLibraryService.CanonicalizeId(sel.ItemId);
            if (byId.TryGetValue(key, out var item) && item.IsArmor && !item.IsShield && !item.IsWeapon)
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

    private static int GetEquippedMagicalWearableAcAdjustment(
        CharacterSheet character,
        IReadOnlyDictionary<string, CustomEquipmentData> byId)
    {
        if (character.EquippedMagicalItemIds is null || character.EquippedMagicalItemIds.Count == 0)
            return 0;

        int total = 0;
        foreach (string itemId in character.EquippedMagicalItemIds)
        {
            string key = EquipmentLibraryService.CanonicalizeId(itemId);
            if (!byId.TryGetValue(key, out var item))
                continue;

            if (item.IsArmor || item.IsShield || item.IsWeapon)
                continue;

            int adjustment = item.ArmorClassModifier;
            if (adjustment == 0 && item.EnchantmentBonus != 0)
                adjustment = -item.EnchantmentBonus;

            total += adjustment;
        }

        return total;
    }

    public static (int EffectiveArmorClass, string Source) DescribeArmorClassSource(
        CustomEquipmentData armor,
        IReadOnlyDictionary<string, CustomEquipmentData>? libraryByName = null)
    {
        // Supports magical entries like "Bracers of Defense AC 6" where source metadata
        // describes a fixed AC value (e.g., "Sets AC to 6") but ArmorClassValue may be unset.
        int fromMetadata = ParseFixedArmorClassFromMetadata(armor.MagicalItemMetadata);
        if (fromMetadata > 0)
            return (fromMetadata, "metadata");

        if ((armor.EnchantmentBonus != 0 || armor.ArmorClassModifier != 0)
            && TryResolveBaseItemArmorClass(armor, libraryByName, out int baseArmorClassOrAdj))
        {
            // Imported magical rows commonly duplicate enhancement into ACMod.
            // Prefer ACMod when provided; otherwise fall back to enhancement.
            int magicalAdjustment = armor.ArmorClassModifier != 0
                ? armor.ArmorClassModifier
                : -armor.EnchantmentBonus;

            if (armor.IsShield)
                return (baseArmorClassOrAdj + magicalAdjustment, "metadata(base+magic)");

            return (Math.Clamp(baseArmorClassOrAdj + magicalAdjustment, 1, 10), "metadata(base+magic)");
        }

        return (armor.ArmorClassValue, "field");
    }

    private static bool TryResolveBaseItemArmorClass(
        CustomEquipmentData armor,
        IReadOnlyDictionary<string, CustomEquipmentData>? libraryByName,
        out int baseArmorClassOrAdjustment)
    {
        baseArmorClassOrAdjustment = armor.IsShield ? -1 : 10;
        if (libraryByName is null || armor.MagicalItemMetadata is null || armor.MagicalItemMetadata.Count == 0)
            return false;

        if (!armor.MagicalItemMetadata.TryGetValue("BaseItemName", out string? baseItemName)
            || string.IsNullOrWhiteSpace(baseItemName))
        {
            return false;
        }

        string key = baseItemName.Trim();
        if (string.Equals(key, (armor.Name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!libraryByName.TryGetValue(key, out var baseItem))
        {
            if (armor.IsShield)
            {
                baseArmorClassOrAdjustment = -1;
                return true;
            }

            int inferred = InferArmorClassFromName(baseItemName);
            if (inferred < 10)
            {
                baseArmorClassOrAdjustment = inferred;
                return true;
            }

            return false;
        }

        if (armor.IsShield && baseItem.IsShield)
        {
            baseArmorClassOrAdjustment = baseItem.ArmorClassValue;
            return true;
        }

        if (armor.IsArmor && !armor.IsShield && baseItem.IsArmor)
        {
            baseArmorClassOrAdjustment = baseItem.ArmorClassValue;
            return true;
        }

        return false;
    }

    private static int InferArmorClassFromName(string? name)
    {
        string n = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(n)) return 10;

        if (n.Contains("full plate")) return 1;
        if (n.Contains("field plate")) return 2;
        if (n.Contains("plate mail")) return 3;
        if (n.Contains("bronze plate")) return 4;
        if (n.Contains("banded")) return 4;
        if (n.Contains("splint")) return 4;
        if (n.Contains("chain mail")) return 5;
        if (n.Contains("scale mail")) return 6;
        if (n.Contains("brigandine")) return 6;
        if (n.Contains("hide")) return 6;
        if (n.Contains("ring mail")) return 7;
        if (n.Contains("studded leather")) return 7;
        if (n.Contains("leather")) return 8;
        if (n.Contains("padded")) return 8;
        if (n.Contains("shield") || n.Contains("buckler")) return -1;
        return 10;
    }

    private static int ParseFixedArmorClassFromMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return 0;

        string[] preferredKeys = { "SetArmorClass", "SetAC", "ArmorClassSet" };
        foreach (string key in preferredKeys)
        {
            if (metadata.TryGetValue(key, out string? value)
                && int.TryParse(value?.Trim(), out int explicitAc)
                && explicitAc >= 1
                && explicitAc <= 10)
            {
                return explicitAc;
            }
        }

        string[] descriptiveKeys = { "EffectList", "EffectNotes", "ShortDescription", "VariantName", "ItemName" };
        foreach (string key in descriptiveKeys)
        {
            if (!metadata.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
                continue;

            var setMatch = SetAcRegex.Match(text);
            if (setMatch.Success
                && int.TryParse(setMatch.Groups["ac"].Value, out int setAc)
                && setAc >= 1
                && setAc <= 10)
            {
                return setAc;
            }

            if (string.Equals(key, "VariantName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "ItemName", StringComparison.OrdinalIgnoreCase))
            {
                var variantMatch = VariantAcRegex.Match(text);
                if (variantMatch.Success
                    && int.TryParse(variantMatch.Groups["ac"].Value, out int variantAc)
                    && variantAc >= 1
                    && variantAc <= 10)
                {
                    return variantAc;
                }
            }
        }

        return 0;
    }

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
