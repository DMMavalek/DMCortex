using System;
using System.Collections.Generic;

namespace DungeonMasterCortex.Services;

/// <summary>
/// Utility class for spellbook operations including type definitions, page calculations, and validation.
/// </summary>
public static class SpellbookUtility
{
    // ── Spellbook Type Definitions ────────────────────────────────────────

    public const string TYPE_TRAVELING = "Traveling";
    public const string TYPE_STANDARD = "Standard";
    public const string TYPE_TOME = "Tome";

    /// <summary>
    /// Spellbook type metadata: name, pages, weight, dimensions.
    /// </summary>
    public static readonly Dictionary<string, (string Name, int Pages, double WeightLbs, string Dimensions)> SpellbookTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [TYPE_TRAVELING] = ("Traveling Spellbook", 50, 3.0, "12\" x 6\" x 1\""),
        [TYPE_STANDARD] = ("Standard Spellbook", 100, 15.0, "16\" x 12\" x 6\""),
        [TYPE_TOME] = ("Tome", 500, 100.0, "20\" x 16\" x 12\""),
    };

    // ── Page Calculation ──────────────────────────────────────────────────

    /// <summary>
    /// Gets the page requirement range for a spell of the given level.
    /// Cantrips: 1d4 (1–4 pages)
    /// Level 1: 1d6 (1–6 pages)
    /// Level 2: 1d6+1 (2–7 pages)
    /// Level 3: 1d6+2 (3–8 pages)
    /// ... and so on (Level N: 1d6+(N-1) for N ≥ 1)
    /// </summary>
    /// <param name="spellLevel">Spell level (0 for cantrip, 1–9 for spell levels)</param>
    /// <returns>Tuple of (min, max) pages required</returns>
    public static (int Min, int Max) GetPageRange(int spellLevel)
    {
        if (spellLevel < 0)
            return (1, 4); // Default to cantrip range for invalid input

        if (spellLevel == 0)
            return (1, 4); // 1d4 for cantrips

        // Level 1+: 1d6+(Level-1)
        int modifier = spellLevel - 1;
        return (1 + modifier, 6 + modifier);
    }

    /// <summary>
    /// Gets a deterministic default page count for a spell level.
    /// Uses the midpoint of the legal range so legacy/imported spells can be
    /// synchronized into spellbooks without requiring a dice roll prompt.
    /// </summary>
    public static int GetDefaultPageCount(int spellLevel)
    {
        var (min, max) = GetPageRange(spellLevel);
        return (min + max) / 2;
    }

    /// <summary>
    /// Validates that a page count falls within the acceptable range for a spell level.
    /// </summary>
    /// <param name="pages">Number of pages entered</param>
    /// <param name="spellLevel">Spell level</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool ValidatePageCount(int pages, int spellLevel)
    {
        var (min, max) = GetPageRange(spellLevel);
        return pages >= min && pages <= max;
    }

    /// <summary>
    /// Gets the validation error message if page count is invalid.
    /// Returns null if valid.
    /// </summary>
    public static string? GetPageValidationError(int pages, int spellLevel)
    {
        var (min, max) = GetPageRange(spellLevel);
        if (pages < min || pages > max)
            return $"Level {spellLevel} spells require {min}–{max} pages (you entered {pages}).";
        return null;
    }

    // ── Spellbook Type Helpers ────────────────────────────────────────────

    /// <summary>
    /// Gets the capacity and weight for a spellbook type.
    /// </summary>
    public static bool TryGetSpellbookInfo(string type, out int pages, out double weight, out string dimensions)
    {
        pages = 100;
        weight = 15.0;
        dimensions = "16\" x 12\" x 6\"";

        if (SpellbookTypes.TryGetValue(type, out var info))
        {
            pages = info.Pages;
            weight = info.WeightLbs;
            dimensions = info.Dimensions;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a spellbook of the given type with default properties.
    /// </summary>
    public static Models.WizardSpellbook CreateSpellbook(string type, string name = "")
    {
        if (!TryGetSpellbookInfo(type, out int pages, out double weight, out string dimensions))
        {
            // Fallback to Standard if type is invalid
            type = TYPE_STANDARD;
            TryGetSpellbookInfo(type, out pages, out weight, out dimensions);
        }

        return new Models.WizardSpellbook
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? SpellbookTypes[type].Name : name,
            Type = type,
            CapacityPages = pages,
            WeightLbs = weight,
            Dimensions = dimensions,
            SpellPages = new Dictionary<string, int>(),
        };
    }

    /// <summary>
    /// Checks if a spell can be added to a spellbook given the page count.
    /// </summary>
    public static bool CanAddSpellToBook(Models.WizardSpellbook book, int pageCount)
    {
        return book.GetAvailablePages() >= pageCount;
    }

    /// <summary>
    /// Adds a spell to a spellbook, tracking its page usage.
    /// Returns false if there isn't enough room.
    /// </summary>
    public static bool TryAddSpellToBook(Models.WizardSpellbook book, string spellId, int pageCount)
    {
        if (!CanAddSpellToBook(book, pageCount))
            return false;

        // If spell already exists, update its page count; otherwise add new entry
        book.SpellPages[spellId] = pageCount;
        return true;
    }

    /// <summary>
    /// Removes a spell from a spellbook.
    /// </summary>
    public static void RemoveSpellFromBook(Models.WizardSpellbook book, string spellId)
    {
        book.SpellPages.Remove(spellId);
    }

    // ── Inventory Sync ────────────────────────────────────────────────────

    /// <summary>
    /// Detects whether a character inventory item is a physical spellbook and returns its type
    /// string (TYPE_TRAVELING, TYPE_STANDARD, or TYPE_TOME), or null if it is not a spellbook.
    /// Detection uses ItemId prefix first, then ItemName keyword matching.
    /// </summary>
    public static string? DetectSpellbookType(string itemId, string itemName)
    {
        // Match by known catalog item IDs (exact or prefix).
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            if (itemId.Equals("spellbook_traveling", StringComparison.OrdinalIgnoreCase)
                || itemId.Equals("spellbook_travelling", StringComparison.OrdinalIgnoreCase))
                return TYPE_TRAVELING;

            if (itemId.Equals("spellbook_tome", StringComparison.OrdinalIgnoreCase))
                return TYPE_TOME;

            if (itemId.Equals("spellbook_standard", StringComparison.OrdinalIgnoreCase))
                return TYPE_STANDARD;
        }

        // Match by item name keywords.
        if (string.IsNullOrWhiteSpace(itemName))
            return null;

        string n = itemName.Trim();
        bool hasSpellbook = n.IndexOf("spellbook", StringComparison.OrdinalIgnoreCase) >= 0;
        bool hasTome      = n.IndexOf("tome", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!hasSpellbook && !hasTome)
            return null;

        if (hasTome)
            return TYPE_TOME;

        if (n.IndexOf("travel", StringComparison.OrdinalIgnoreCase) >= 0)
            return TYPE_TRAVELING;

        return TYPE_STANDARD;
    }

    /// <summary>
    /// Ensures a character's <see cref="Models.CharacterSheet.WizardSpellbooks"/> list contains
    /// an entry for every spellbook-type item in their inventory
    /// (<see cref="Models.CharacterSheet.EquipmentSelections"/>).
    /// Creates new <see cref="Models.WizardSpellbook"/> entries as needed and links them via
    /// <see cref="Models.WizardSpellbook.InventoryItemId"/>.
    /// Returns true when at least one new book was added (indicating the caller should save).
    /// </summary>
    public static bool SyncSpellbooksFromInventory(Models.CharacterSheet character)
    {
        if (character is null) return false;

        character.WizardSpellbooks ??= new System.Collections.Generic.List<Models.WizardSpellbook>();
        character.EquipmentSelections ??= new System.Collections.Generic.List<Models.EquipmentSelection>();

        bool changed = false;

        foreach (var eq in character.EquipmentSelections)
        {
            string? bookType = DetectSpellbookType(eq.ItemId ?? "", eq.ItemName ?? "");
            if (bookType is null)
                continue;

            // Duplicate quantity: each unit is its own physical book.
            int qty = Math.Max(1, eq.Quantity);
            // Count how many WizardSpellbooks already reference this inventory ItemId.
            int alreadyLinked = character.WizardSpellbooks
                .Count(b => string.Equals(b.InventoryItemId, eq.ItemId, StringComparison.OrdinalIgnoreCase));

            for (int i = alreadyLinked; i < qty; i++)
            {
                // Pick a display name: use the inventory item's ItemName, with a suffix for multiples.
                string baseName = string.IsNullOrWhiteSpace(eq.ItemName) ? SpellbookTypes[bookType].Name : eq.ItemName.Trim();
                int existingWithSameName = character.WizardSpellbooks
                    .Count(b => b.Name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase));
                string name = existingWithSameName == 0 ? baseName : $"{baseName} {existingWithSameName + 1}";

                var newBook = CreateSpellbook(bookType, name);
                newBook.InventoryItemId = eq.ItemId ?? "";
                character.WizardSpellbooks.Add(newBook);
                changed = true;
            }
        }

        // Remove any auto-generated placeholder books that have no spells and no inventory link,
        // but only when at least one inventory-linked book now exists.
        bool anyLinked = character.WizardSpellbooks.Any(b => !string.IsNullOrWhiteSpace(b.InventoryItemId));
        if (anyLinked)
        {
            int before = character.WizardSpellbooks.Count;
            character.WizardSpellbooks = character.WizardSpellbooks
                .Where(b => !string.IsNullOrWhiteSpace(b.InventoryItemId)
                            || (b.SpellPages?.Count ?? 0) > 0)
                .ToList();
            if (character.WizardSpellbooks.Count != before)
                changed = true;
        }

        return changed;
    }
}
