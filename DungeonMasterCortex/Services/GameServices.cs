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

    public void ApplyDamage(Combatant target, int damage)
    {
        if (Current is null) return;
        target.HpCurrent = System.Math.Max(0, target.HpCurrent - damage);
    }

    public void AdvanceRound()
    {
        if (Current is null) return;
        Current.RoundNumber++;
    }
}

public class CampaignService
{
    public List<CampaignEntry>  Entries   { get; } = new();
    public List<NpcEntry>       Npcs      { get; } = new();
    public List<LocationEntry>  Locations { get; } = new();

    public void AddEntry(CampaignEntry entry) => Entries.Add(entry);
}
