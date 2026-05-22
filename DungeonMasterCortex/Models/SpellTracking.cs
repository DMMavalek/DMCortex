using System;
using System.Collections.Generic;

namespace DungeonMasterCortex.Models;

/// <summary>
/// Represents a single spell cast during a day. Tracks the spell, when it was cast, and any notes.
/// </summary>
public class CastSpellRecord
{
    public string SpellId { get; set; } = "";
    public string SpellName { get; set; } = "";
    public int SpellLevel { get; set; } = 0;
    public DateTime CastTime { get; set; } = DateTime.Now;
    public string Notes { get; set; } = "";
}

/// <summary>
/// Represents a spell prepared by a priest for the day. Priests must select spells from their available spheres.
/// </summary>
public class PreparedSpellRecord
{
    public string SpellId { get; set; } = "";
    public string SpellName { get; set; } = "";
    public int SpellLevel { get; set; } = 0;
    public string Sphere { get; set; } = "";  // Priest sphere (e.g., "Healing", "War", "Magic")
    public bool IsCast { get; set; } = false;  // Whether this prepared spell has been cast
}

/// <summary>
/// Represents spell tracking for a single day (campaign or session day).
/// Tracks prepared spells (for priests) and cast spells (for all casters).
/// </summary>
public class DailySpellTracking
{
    // Reference to campaign calendar date (if linked)
    public int Year { get; set; } = 0;
    public string Era { get; set; } = "PC";
    public int Month { get; set; } = 0;      // 0-indexed
    public int Day { get; set; } = 1;        // 1-indexed

    // Spells prepared at the start of the day (for priests/psionicists)
    public List<PreparedSpellRecord> PreparedSpells { get; set; } = new();

    // Spells cast during the day
    public List<CastSpellRecord> CastSpells { get; set; } = new();

    // Session notes for this day (personal tracker, not affecting DM calendar)
    public string SessionNotes { get; set; } = "";

    // Timestamp when this day's tracking was created
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public DateTime LastModified { get; set; } = DateTime.Now;

    // Helper: get a date key for lookup
    public string GetDateKey() => $"{Year:D4}_{Era}_{Month:D2}_{Day:D2}";
}

/// <summary>
/// Priest/Psionicist spell casting configuration for a character.
/// Handles daily memorization, sphere selection, and casting limits.
/// </summary>
public class DivineSpellConfiguration
{
    // Class type that uses divine spells (e.g., "Priest", "Psionicist", "Druid")
    public string CasterClassId { get; set; } = "";

    // Available spell slots per level (calculated from character level and class)
    // Key: spell level (1-7), Value: number of slots available
    public Dictionary<int, int> MaxSpellSlots { get; set; } = new();

    // Selected spheres for priest memorization (Key: sphere name)
    public List<string> SelectedSpheres { get; set; } = new();

    // If true, priest can cast any known spell without daily memorization
    // (allows "prayer-based" casting instead of memorization)
    public bool BypassMemorization { get; set; } = false;

    // Psionicist power points (if applicable)
    public int PowerPoints { get; set; } = 0;
    public int MaxPowerPoints { get; set; } = 0;
}

/// <summary>
/// Wizard spell casting configuration for a character.
/// Handles spellbook management, spell lists, and available spells.
/// </summary>
public class ArcaneSpellConfiguration
{
    // Available spell slots per level (calculated from character level and class)
    // Key: spell level (1-9), Value: number of slots available
    public Dictionary<int, int> MaxSpellSlots { get; set; } = new();

    // Selected wizard schools (Key: school name, Value: is selected)
    public Dictionary<string, bool> SelectedSchools { get; set; } = new();

    // Active spellbook currently being used for preparation (ID reference)
    public string ActiveSpellbookId { get; set; } = "";

    // Bonus spells from high INT (calculated separately)
    public Dictionary<int, int> BonusSpells { get; set; } = new();
}

/// <summary>
/// Complete spell tracking system for a character.
/// Manages daily spell tracking, configurations, and history.
/// </summary>
public class CharacterSpellTracking
{
    public string CharacterId { get; set; } = "";
    public string CharacterName { get; set; } = "";

    // Player-defined tags per spell id for custom sorting/filtering.
    public Dictionary<string, List<string>> SpellUserTags { get; set; } = new();

    // Divine spell configuration (priests, druids, psionicists)
    public DivineSpellConfiguration? DivineConfig { get; set; }

    // Arcane spell configuration (wizards, sorcerers)
    public ArcaneSpellConfiguration? ArcaneConfig { get; set; }

    // Current day's spell tracking
    public DailySpellTracking CurrentDayTracking { get; set; } = new();

    // Historical spell tracking (all prior days)
    public List<DailySpellTracking> TrackingHistory { get; set; } = new();

    // Linked campaign calendar (if available)
    // Stores reference to campaign ID so player can sync with DM's calendar
    public string LinkedCampaignId { get; set; } = "";
    public string LinkedCalendarId { get; set; } = "";

    // Last synced date from campaign calendar
    public DateTime LastSyncedDate { get; set; } = DateTime.MinValue;

    // Timestamp when tracking was created
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public DateTime LastModified { get; set; } = DateTime.Now;

    /// <summary>
    /// Get current available spell slots (accounting for cast spells).
    /// Returns slots remaining for each spell level.
    /// </summary>
    public Dictionary<int, int> GetAvailableSpellSlots(bool isDivine)
    {
        Dictionary<int, int> maxSlots;
        if (isDivine && DivineConfig != null)
            maxSlots = DivineConfig.MaxSpellSlots;
        else if (!isDivine && ArcaneConfig != null)
            maxSlots = ArcaneConfig.MaxSpellSlots;
        else
            return new Dictionary<int, int>();

        var available = new Dictionary<int, int>(maxSlots);

        // Subtract cast spells from today
        foreach (var castSpell in CurrentDayTracking.CastSpells)
        {
            if (available.ContainsKey(castSpell.SpellLevel))
                available[castSpell.SpellLevel]--;
        }

        // Ensure no negative slots
        foreach (var level in new List<int>(available.Keys))
            if (available[level] < 0)
                available[level] = 0;

        return available;
    }

    /// <summary>
    /// Reset spells for a new day (memorize new spells, clear cast list).
    /// </summary>
    public void ResetForNewDay(int year, string era, int month, int day)
    {
        // Save current day to history
        if (CurrentDayTracking.CastSpells.Count > 0 || !string.IsNullOrEmpty(CurrentDayTracking.SessionNotes))
            TrackingHistory.Add(CurrentDayTracking);

        // Create new day tracking
        CurrentDayTracking = new DailySpellTracking
        {
            Year = year,
            Era = era,
            Month = month,
            Day = day,
            CreatedOn = DateTime.Now
        };

        LastModified = DateTime.Now;
    }

    /// <summary>
    /// Record a spell as cast, removing it from available slots.
    /// </summary>
    public void CastSpell(string spellId, string spellName, int spellLevel, string notes = "")
    {
        CurrentDayTracking.CastSpells.Add(new CastSpellRecord
        {
            SpellId = spellId,
            SpellName = spellName,
            SpellLevel = spellLevel,
            CastTime = DateTime.Now,
            Notes = notes
        });
        LastModified = DateTime.Now;
    }

    /// <summary>
    /// Uncast a spell (restore it to available slots).
    /// </summary>
    public void UncastSpell(int castSpellIndex)
    {
        if (castSpellIndex >= 0 && castSpellIndex < CurrentDayTracking.CastSpells.Count)
        {
            CurrentDayTracking.CastSpells.RemoveAt(castSpellIndex);
            LastModified = DateTime.Now;
        }
    }

    /// <summary>
    /// Add a prepared spell for the day (priest memorization).
    /// </summary>
    public void AddPreparedSpell(string spellId, string spellName, int spellLevel, string sphere)
    {
        CurrentDayTracking.PreparedSpells.Add(new PreparedSpellRecord
        {
            SpellId = spellId,
            SpellName = spellName,
            SpellLevel = spellLevel,
            Sphere = sphere,
            IsCast = false
        });
        LastModified = DateTime.Now;
    }

    /// <summary>
    /// Mark a prepared spell as cast.
    /// </summary>
    public void CastPreparedSpell(int preparedIndex)
    {
        if (preparedIndex >= 0 && preparedIndex < CurrentDayTracking.PreparedSpells.Count)
        {
            CurrentDayTracking.PreparedSpells[preparedIndex].IsCast = true;
            LastModified = DateTime.Now;
        }
    }

    /// <summary>
    /// Get tracking for a specific historical day.
    /// </summary>
    public DailySpellTracking? GetHistoricalTracking(int year, string era, int month, int day)
    {
        string key = $"{year:D4}_{era}_{month:D2}_{day:D2}";
        return TrackingHistory.FirstOrDefault(t => t.GetDateKey() == key);
    }
}
