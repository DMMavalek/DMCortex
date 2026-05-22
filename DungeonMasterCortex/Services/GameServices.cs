using System.Collections.Generic;
using System.Linq;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public class CombatService
{
    public Encounter? Current { get; private set; }

    public void NewEncounter(string name)
    {
        Current = new Encounter { Name = name, RoundNumber = 1 };
    }

    public void AddCombatant(Combatant c)
    {
        if (Current is null) return;
        Current.Combatants.Add(c);
        Current.Combatants = Current.Combatants
            .OrderByDescending(x => x.Initiative)
            .ToList();
    }

    public bool RemoveCombatant(string combatantId)
    {
        if (Current is null) return false;

        int index = Current.Combatants.FindIndex(c => string.Equals(c.CombatantId, combatantId, System.StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        Current.Combatants.RemoveAt(index);
        return true;
    }

    public void ApplyDamage(Combatant target, int damage)
    {
        if (Current is null) return;
        target.HpCurrent = System.Math.Max(-10, target.HpCurrent - damage);
    }

    public void AdvanceRound()
    {
        if (Current is null) return;
        Current.RoundNumber++;
    }
}

public class CampaignService
{
    public string CampaignId { get; set; } = "default";
    public string CampaignName { get; set; } = "Default Campaign";
    public string SharedCalendarSourceCampaignId { get; set; } = "";

    public List<CampaignEntry>  Entries   { get; } = new();
    public List<NpcEntry>       Npcs      { get; } = new();
    public List<LocationEntry>  Locations { get; } = new();
    public List<PartyLootEntry> PartyLoot { get; } = new();
    
    // Calendar system
    public Calendar Calendar { get; } = new();

    public void AddEntry(CampaignEntry entry) => Entries.Add(entry);
    public void AddPartyLoot(PartyLootEntry loot) => PartyLoot.Add(loot);
    public void RemovePartyLoot(PartyLootEntry loot) => PartyLoot.Remove(loot);

    /// <summary>
    /// Log a combat encounter to the campaign calendar
    /// </summary>
    public void LogCombatEncounter(string encounterName, int numCombatants)
    {
        var evt = new CalendarEvent
        {
            EventType = "Combat",
            Title = encounterName,
            Description = $"Combat encounter with {numCombatants} combatants",
            RelatedId = encounterName
        };
        Calendar.AddEvent(Calendar.CurrentMonth, Calendar.CurrentDay, evt);
    }

    /// <summary>
    /// Award treasure to the party with calendar tracking
    /// </summary>
    public void AwardTreasure(string treasureTitle, string treasureDescription, string source, string sessionId = "")
    {
        var evt = new CalendarEvent
        {
            EventType = "Treasure",
            Title = treasureTitle,
            Description = treasureDescription,
            RelatedId = source
        };
        Calendar.AddEvent(Calendar.CurrentMonth, Calendar.CurrentDay, evt);

        var loot = new PartyLootEntry
        {
            Name = treasureTitle,
            Description = treasureDescription,
            Source = $"{Calendar.GetCurrentDateDisplay()} - {source}",
            SessionId = sessionId,
            Quantity = 1
        };
        AddPartyLoot(loot);
    }
}
