using System;
using System.Collections.Generic;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class CharacterWealthService
{
    private static readonly Dictionary<string, (int DiceCount, int DiceSides, int Multiplier)> StartingGoldByClass =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fighter"] = (5, 4, 10),
            ["paladin"] = (5, 4, 10),
            ["ranger"] = (5, 4, 10),
            ["cleric"] = (3, 6, 10),
            ["druid"] = (3, 6, 10),
            ["thief"] = (2, 6, 10),
            ["bard"] = (3, 6, 10),
            ["wizard"] = (2, 4, 10),
            ["psionicist"] = (3, 4, 10),
        };

    public static int ToCopper(int gp, int sp, int cp)
        => Math.Max(0, gp) * 100 + Math.Max(0, sp) * 10 + Math.Max(0, cp);

    public static void FromCopper(int totalCopper, out int gp, out int sp, out int cp)
    {
        int value = Math.Max(0, totalCopper);
        gp = value / 100;
        value %= 100;
        sp = value / 10;
        cp = value % 10;
    }

    public static int GetTotalCopper(CharacterSheet character)
        => ToCopper(character.GoldPieces, character.SilverPieces, character.CopperPieces);

    public static void SetFromCopper(CharacterSheet character, int totalCopper)
    {
        FromCopper(totalCopper, out int gp, out int sp, out int cp);
        character.GoldPieces = gp;
        character.SilverPieces = sp;
        character.CopperPieces = cp;
    }

    public static bool TrySpend(CharacterSheet character, int gp, int sp, int cp, out string reason)
    {
        int spendCopper = ToCopper(gp, sp, cp);
        int available = GetTotalCopper(character);
        if (spendCopper <= 0)
        {
            reason = "Spend amount must be greater than zero.";
            return false;
        }
        if (available < spendCopper)
        {
            reason = $"Not enough funds. Need {FormatCoins(spendCopper)}, have {FormatCoins(available)}.";
            return false;
        }

        SetFromCopper(character, available - spendCopper);
        reason = string.Empty;
        return true;
    }

    public static void Add(CharacterSheet character, int gp, int sp, int cp)
    {
        int addCopper = ToCopper(gp, sp, cp);
        int total = GetTotalCopper(character);
        SetFromCopper(character, total + addCopper);
    }

    public static bool EnsureStartingFunds(CharacterSheet character)
    {
        if (character.StartingFundsAssigned)
            return false;

        string classId = character.ClassIds.Count > 0
            ? character.ClassIds[0]
            : character.ClassId;

        int startingGold = RollStartingGoldGp(classId);
        Add(character, startingGold, 0, 0);
        character.StartingFundsAssigned = true;
        return true;
    }

    public static int RollStartingGoldGp(string? classId)
    {
        var rollSpec = StartingGoldByClass.TryGetValue((classId ?? string.Empty).Trim(), out var spec)
            ? spec
            : (DiceCount: 3, DiceSides: 6, Multiplier: 10);

        int total = 0;
        for (int i = 0; i < rollSpec.DiceCount; i++)
            total += Random.Shared.Next(1, rollSpec.DiceSides + 1);
        return total * rollSpec.Multiplier;
    }

    public static string FormatCoins(int totalCopper)
    {
        FromCopper(totalCopper, out int gp, out int sp, out int cp);
        return $"{gp} gp, {sp} sp, {cp} cp";
    }

    public static string FormatCoins(CharacterSheet character)
        => $"{Math.Max(0, character.GoldPieces)} gp, {Math.Max(0, character.SilverPieces)} sp, {Math.Max(0, character.CopperPieces)} cp";
}
