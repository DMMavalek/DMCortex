using System;
using System.Collections.Generic;

namespace DungeonMasterCortex.Services;

/// <summary>
/// Look-up tables for PO sub-ability scores → game effects.
/// Based on AD&D 2e Player's Option: Skills &amp; Powers tables.
/// </summary>
public static class SubAbilityTables
{
    // ── Public entry-point ──────────────────────────────────────────────────
    /// <summary>
    /// Returns a compact effect string for display in the character generator.
    /// <paramref name="subKey"/> is e.g. "str_muscle", "wis_perception".
    /// <paramref name="exceptionalStr"/> is 1–100 for warriors with STR 18, else 0.
    /// </summary>
    public static string GetEffect(string subKey, int score, int exceptionalStr = 0)
    {
        return subKey switch
        {
            "str_muscle"     => MuscleEffect(score, exceptionalStr),
            "str_stamina"    => StaminaEffect(score, exceptionalStr),
            "dex_aim"        => AimEffect(score),
            "dex_balance"    => BalanceEffect(score),
            "con_health"     => HealthEffect(score),
            "con_fitness"    => FitnessEffect(score),
            "int_reason"     => ReasonEffect(score),
            "int_knowledge"  => KnowledgeEffect(score),
            "wis_intuition"  => IntuitionEffect(score),
            "wis_willpower"  => WillpowerEffect(score),
            "wis_perception" => PerceptionEffect(score),
            "cha_leadership" => LeadershipEffect(score),
            "cha_appearance" => AppearanceEffect(score),
            _ => ""
        };
    }

    /// <summary>
    /// Aggregates all selected sub-abilities into concrete mechanical totals.
    /// Keys are expected in full form, e.g. "str_muscle", "wis_perception".
    /// </summary>
    public static SubAbilityMechanicalTotals CalculateTotals(
        IReadOnlyDictionary<string, int> subAbilities,
        int exceptionalStr = 0,
        string? classId = null)
    {
        var t = new SubAbilityMechanicalTotals();

        foreach (var (key, score) in subAbilities)
        {
            switch (key)
            {
                case "str_muscle":
                {
                    var (atk, dmg, press, openDoors, bendBars) = MuscleData(score, exceptionalStr);
                    t.MeleeAttackBonus += atk;
                    t.MeleeDamageBonus += dmg;
                    t.MaxPressLbs = Math.Max(t.MaxPressLbs, press);
                    t.OpenDoorsScore = Math.Max(t.OpenDoorsScore, openDoors);
                    t.BendBarsPercent = Math.Max(t.BendBarsPercent, bendBars);
                    break;
                }
                case "str_stamina":
                    t.CarryCapacityLbs = Math.Max(t.CarryCapacityLbs, StaminaCarry(score, exceptionalStr));
                    break;
                case "dex_aim":
                {
                    var (missileAdj, pickPocketsAdj, openLocksAdj) = AimData(score);
                    t.MissileAttackBonus += missileAdj;
                    t.PickPocketsAdjustment += pickPocketsAdj;
                    t.OpenLocksAdjustment += openLocksAdj;
                    break;
                }
                case "dex_balance":
                {
                    var (reactionAdj, defenseAdj, moveSilentlyAdj, climbWallsAdj) = BalanceData(score);
                    t.ReactionAdjustment += reactionAdj;
                    t.ArmorClassAdjustment += defenseAdj;
                    t.DefensiveAdjustment += defenseAdj;
                    t.MoveSilentlyAdjustment += moveSilentlyAdj;
                    t.ClimbWallsAdjustment += climbWallsAdj;
                    break;
                }
                case "con_health":
                {
                    var (sys, poisonSaveAdj) = HealthData(score);
                    t.SystemShockPercent = Math.Max(t.SystemShockPercent, sys);
                    t.PoisonSaveAdjustment += poisonSaveAdj;
                    break;
                }
                case "con_fitness":
                {
                    var (hpPerLevel, resurrectionChance) = FitnessData(score, classId);
                    t.HpPerLevel += hpPerLevel;
                    t.ResurrectionPercent = Math.Max(t.ResurrectionPercent, resurrectionChance);
                    break;
                }
                case "int_reason":
                {
                    var (spellLevel, maxPerLevel, spellImmunityPct) = ReasonData(score);
                    t.SpellLevelMaximum = Math.Max(t.SpellLevelMaximum, spellLevel);
                    t.MaxSpellsPerLevel = Math.Max(t.MaxSpellsPerLevel, maxPerLevel);
                    t.SpellImmunityPercent = Math.Max(t.SpellImmunityPercent, spellImmunityPct);
                    break;
                }
                case "int_knowledge":
                {
                    var (langs, nwp, learnSpellPct) = KnowledgeData(score);
                    t.ExtraLanguages = Math.Max(t.ExtraLanguages, langs);
                    t.BonusNwpSlots += nwp;
                    t.LearnSpellsPercent = Math.Max(t.LearnSpellsPercent, learnSpellPct);
                    break;
                }
                case "wis_intuition":
                {
                    var (bonusPriest, priestFailPct) = IntuitionData(score);
                    t.BonusPriestSpells = bonusPriest;
                    t.PriestSpellFailurePercent = priestFailPct;
                    break;
                }
                case "wis_willpower":
                {
                    var (magicDefAdj, spellImmunityPct) = WillpowerData(score);
                    t.MagicDefenseAdjustment += magicDefAdj;
                    t.SpellImmunityPercent = Math.Max(t.SpellImmunityPercent, spellImmunityPct);
                    break;
                }
                case "wis_perception":
                {
                    var (surprise, detectHiddenIn6, hearNoisePct) = PerceptionData(score);
                    t.SurpriseAdjustment += surprise;
                    t.DetectHiddenIn6 = Math.Max(t.DetectHiddenIn6, detectHiddenIn6);
                    t.HearNoisePercent = Math.Max(t.HearNoisePercent, hearNoisePct);
                    break;
                }
                case "cha_leadership":
                {
                    var (maxHench, loyalty, react) = LeadershipData(score);
                    t.MaxHenchmen = Math.Max(t.MaxHenchmen, maxHench);
                    t.LoyaltyAdjustment += loyalty;
                    t.ReactionAdjustment += react;
                    break;
                }
                case "cha_appearance":
                    t.ReactionAdjustment += AppearanceReaction(score);
                    break;
            }
        }

        return t;
    }

    // ── STR: Muscle ─────────────────────────────────────────────────────────
    // Attack adjustment, damage adjustment, Open Doors, Bend Bars/Lift Gates.
    // Uses PHB-style Strength breakpoints for score rows used by the UI.
    private static string MuscleEffect(int score, int exStr)
    {
        // If score is 18 and we have an exceptional-str roll, use fine-grained table
        var (a, d, mp, od, bb) = MuscleData(score, exStr);
        return $"Atk: {Fmt(a)}, Dmg: {Fmt(d)}, Open doors: {od}, Bend bars: {bb}%, Max press: {mp} lbs";
    }

    private static (int atk, int dmg, int press, int openDoors, int bendBars) MuscleData(int score, int exStr)
    {
        if (score == 18 && exStr > 0)
            return ExceptionalStrBonus(exStr);

        return score switch
        {
            1        => (-5, -4, 3,   1,  0),
            2        => (-3, -2, 5,   1,  0),
            3        => (-3, -1, 10,  2,  0),
            4 or 5   => (-2, -1, 25,  3,  0),
            6 or 7   => (-1, 0, 55,   4,  0),
            8 or 9   => (0,  0, 90,   5,  1),
            10 or 11 => (0,  0, 115,  6,  2),
            12 or 13 => (0,  0, 140,  7,  4),
            14 or 15 => (0,  0, 170,  8,  7),
            16       => (0,  +1, 195, 9, 10),
            17       => (+1, +1, 220, 10, 13),
            18       => (+1, +2, 255, 11, 16),
            19       => (+3, +7, 640, 16, 50),
            20       => (+3, +8, 700, 17, 60),
            _        => (0, 0, 0, 0, 0)
        };
    }

    private static (int atk, int dmg, int press, int openDoors, int bendBars) ExceptionalStrBonus(int pct) =>
        pct <= 50  ? (+1, +3, 280, 12, 20) :
        pct <= 75  ? (+2, +3, 305, 13, 25) :
        pct <= 90  ? (+2, +4, 330, 14, 30) :
        pct <= 99  ? (+2, +5, 380, 15, 35) :
                     (+3, +6, 480, 16, 40);          // 00 = 100

    // ── STR: Stamina ────────────────────────────────────────────────────────
    // Weight allowance (lbs).
    private static string StaminaEffect(int score, int exStr)
    {
        int wt = StaminaCarry(score, exStr);
        return $"Carry: {wt} lbs";
    }

    private static int StaminaCarry(int score, int exStr = 0)
    {
        if (score == 18 && exStr > 0)
        {
            return exStr switch
            {
                <= 50 => 135,
                <= 75 => 160,
                <= 90 => 185,
                <= 99 => 235,
                _ => 335,
            };
        }

        return score switch
        {
            1        => 5,
            2        => 5,
            3        => 5,
            4 or 5   => 10,
            6 or 7   => 20,
            8 or 9   => 35,
            10 or 11 => 40,
            12 or 13 => 45,
            14 or 15 => 55,
            16       => 70,
            17       => 85,
            18       => 110,
            19       => 485,
            20       => 535,
            _        => 0
        };
    }

    // ── DEX: Aim ────────────────────────────────────────────────────────────
    // Missile adjustment, Pick Pockets, Open Locks.
    private static string AimEffect(int score)
    {
        var (missileAdj, pickPocketsAdj, openLocksAdj) = AimData(score);
        return $"Missile: {Fmt(missileAdj)}, Pick pockets: {Fmt(pickPocketsAdj)}%, Open locks: {Fmt(openLocksAdj)}%";
    }

    private static (int missileAdj, int pickPocketsAdj, int openLocksAdj) AimData(int score)
    {
        int adj = score switch
        {
            1        => -6,
            2        => -4,
            3        => -3,
            4 or 5   => -2,
            6 or 7   => -1,
            8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 => 0,
            16       => +1,
            17 or 18 => +2,
            19 or 20 => +3,
            _        => 0
        };

        // Keep lockpicking/pickpocketing tied to the same dexterity accuracy band.
        int skillAdj = adj;
        return (adj, skillAdj, skillAdj);
    }

    // ── DEX: Balance ────────────────────────────────────────────────────────
    // Reaction adjustment, defensive adjustment, Move Silently, Climb Walls.
    private static string BalanceEffect(int score)
    {
        var (reactionAdj, defenseAdj, moveSilentlyAdj, climbWallsAdj) = BalanceData(score);
        return $"Reaction: {Fmt(reactionAdj)}, Defense: {Fmt(defenseAdj)}, Move silently: {Fmt(moveSilentlyAdj)}%, Climb walls: {Fmt(climbWallsAdj)}%";
    }

    private static (int reactionAdj, int defenseAdj, int moveSilentlyAdj, int climbWallsAdj) BalanceData(int score)
    {
        int reactionAdj = score switch
        {
            1 => -6,
            2 => -4,
            3 => -3,
            4 => -2,
            5 => -1,
            6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 => 0,
            16 => +1,
            17 or 18 => +2,
            19 or 20 => +3,
            _ => 0
        };

        int defenseAdj = score switch
        {
            1 or 2 => +5,
            3 => +4,
            4 => +3,
            5 => +2,
            6 => +1,
            7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 => 0,
            15 => -1,
            16 => -2,
            17 => -3,
            18 or 19 or 20 => -4,
            _ => 0
        };

        int skillAdj = score switch
        {
            <= 3 => -4,
            <= 5 => -3,
            <= 8 => -2,
            <= 14 => 0,
            15 => +1,
            16 => +2,
            17 => +3,
            18 => +4,
            _ => +5
        };

        return (reactionAdj, defenseAdj, skillAdj, skillAdj);
    }

    // ── CON: Health ─────────────────────────────────────────────────────────
    // System shock survival %, poison save adjustment.
    private static string HealthEffect(int score)
    {
        var (sys, poisonSaveAdj) = HealthData(score);
        return $"Sys shock: {sys}%, Poison save: {Fmt(poisonSaveAdj)}";
    }

    private static (int systemShock, int poisonSaveAdj) HealthData(int score)
    {
        int systemShock = score switch
        {
            1 => 25,
            2 => 30,
            3 => 35,
            4 => 40,
            5 => 45,
            6 => 50,
            7 => 55,
            8 => 60,
            9 => 65,
            10 => 70,
            11 => 75,
            12 => 80,
            13 => 85,
            14 => 88,
            15 => 90,
            16 => 95,
            17 => 97,
            _ => 99
        };

        int poisonSaveAdj = score switch
        {
            <= 18 => 0,
            <= 20 => +1,
            _ => +2
        };

        return (systemShock, poisonSaveAdj);
    }

    // ── CON: Fitness ────────────────────────────────────────────────────────
    // HP bonus per level, resurrection chance.
    private static string FitnessEffect(int score)
    {
        var (hp, resurrectionChance) = FitnessData(score);
        return $"HP/level: {Fmt(hp)}, Resurrection: {resurrectionChance}%";
    }

    private static (int hpPerLevel, int resurrectionChance) FitnessData(int score, string? classId = null)
    {
        // Base HP per level for all classes
        int hp = score switch
        {
            1        => -3,
            2 or 3   => -2,
            4 or 5 or 6 => -1,
            _ when score >= 7 && score <= 14 => 0,
            15 => +1,
            16 or 17 or 18 or 19 or 20 => +2,
            _ => +2
        };

        // PHB Table 7: Warriors (fighters) get bonus HP at high Fitness scores
        // Parenthetical bonuses apply only to warrior characters.
        if (string.Equals(classId, "fighter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "paladin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "ranger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "warrior", StringComparison.OrdinalIgnoreCase))
        {
            hp = score switch
            {
                1        => -3,
                2 or 3   => -2,
                4 or 5 or 6 => -1,
                _ when score >= 7 && score <= 14 => 0,
                15 => +1,
                16 => +2,
                17 => +3,  // Warriors get +3 at CON 17 (vs +2 for others)
                18 => +4,  // Warriors get +4 at CON 18 (vs +2 for others)
                19 => +5,  // Warriors get +5 at CON 19
                _ => +5    // Warriors get +5 at CON 20+
            };
        }

        int resurrectionChance = score switch
        {
            <= 3 => 35,
            4 => 45,
            5 => 50,
            6 => 55,
            7 => 60,
            8 => 65,
            9 => 70,
            10 => 75,
            11 => 80,
            12 => 85,
            13 => 90,
            14 => 92,
            15 => 94,
            16 => 96,
            17 => 98,
            _ => 100
        };

        return (hp, resurrectionChance);
    }

    // ── INT: Reason ─────────────────────────────────────────────────────────
    // Spell level, max spells, spell immunity.
    private static string ReasonEffect(int score)
    {
        var (spellLevelMax, maxSpl, spellImmunityPct) = ReasonData(score);
        return $"Spell level: {spellLevelMax}, Max spells/level: {maxSpl}, Spell immunity: {spellImmunityPct}%";
    }

    private static (int spellLevelMax, int maxSpellsPerLevel, int spellImmunityPct) ReasonData(int score)
    {
        int spellLevelMax = score switch
        {
            <= 8 => 0,
            9 => 4,
            10 or 11 => 5,
            12 or 13 => 6,
            14 or 15 => 7,
            16 or 17 => 8,
            _ => 9
        };

        int maxSpellsPerLevel = score switch
        {
            <= 8 => 0,
            9 => 6,
            10 or 11 or 12 => 7,
            13 or 14 => 9,
            15 or 16 => 11,
            17 => 14,
            18 => 18,
            _ => 99
        };

        int spellImmunityPct = score switch
        {
            <= 18 => 0,
            19 => 10,
            20 => 20,
            _ => 25
        };

        return (spellLevelMax, maxSpellsPerLevel, spellImmunityPct);
    }

    // ── INT: Knowledge ──────────────────────────────────────────────────────
    // Bonus proficiencies (languages + NWP), % learn spell.
    private static string KnowledgeEffect(int score)
    {
        var (lang, nwp, learnSpellPct) = KnowledgeData(score);
        return $"Extra languages: {lang}, Bonus NWP slots: {nwp}, Learn spell: {learnSpellPct}%";
    }

    private static (int extraLanguages, int bonusNwpSlots, int learnSpellPct) KnowledgeData(int score)
    {
        return score switch
        {
            1 => (0, 0, 0),
            2 or 3 or 4 or 5 or 6 or 7 or 8 => (1, 0, 0),
            9 => (2, 0, 35),
            10 or 11 => (2, 0, 40 + (score - 10) * 5),
            12 => (3, 1, 50),
            13 => (3, 1, 55),
            14 => (4, 1, 60),
            15 => (4, 1, 65),
            16 => (5, 2, 70),
            17 => (6, 2, 75),
            18          => (7, 3, 85),
            19          => (8, 3, 95),
            _           => (9, 4, 96)
        };
    }

    // ── WIS: Intuition ──────────────────────────────────────────────────────
    // Bonus priest spells, % chance of priest spell failure.
    private static string IntuitionEffect(int score)
    {
        var (spells, failPct) = IntuitionData(score);
        return $"Bonus priest spells: {spells}, Priest spell failure: {failPct}%";
    }

    private static (string bonusPriestSpells, int priestSpellFailurePercent) IntuitionData(int score)
    {
        string bonusSpells = score switch
        {
            <= 12 => "none",
            13 or 14 => "1st",
            15 or 16 => "2nd",
            17 => "3rd",
            18 => "4th",
            19 => "1st, 3rd",
            _ => "2nd, 4th"
        };

        int failPct = score switch
        {
            1 => 80,
            2 => 60,
            3 => 50,
            4 => 45,
            5 => 40,
            6 => 35,
            7 => 30,
            8 => 25,
            9 => 20,
            10 => 15,
            11 => 10,
            12 => 5,
            _ => 0
        };

        return (bonusSpells, failPct);
    }

    // ── WIS: Willpower ──────────────────────────────────────────────────────
    // Magic defense adjustment, spell immunity.
    private static string WillpowerEffect(int score)
    {
        var (magicDefAdj, spellImmunityPct) = WillpowerData(score);
        return $"Magic defense: {Fmt(magicDefAdj)}, Spell immunity: {spellImmunityPct}%";
    }

    private static (int magicDefenseAdj, int spellImmunityPct) WillpowerData(int score)
    {
        int magicDefenseAdj = score switch
        {
            1 => -6,
            2 => -4,
            3 => -3,
            4 => -2,
            5 or 6 or 7 => -1,
            8 or 9 or 10 or 11 or 12 or 13 or 14 => 0,
            15 => +1,
            16 => +2,
            17 => +3,
            _ => +4
        };

        int spellImmunityPct = score switch
        {
            <= 8 => 0,
            <= 12 => 5,
            <= 14 => 10,
            <= 16 => 15,
            <= 18 => 20,
            _ => 25
        };

        return (magicDefenseAdj, spellImmunityPct);
    }

    // ── WIS: Perception ─────────────────────────────────────────────────────
    // Surprise modifier, detect hidden/secret, hear noise.
    // (PO: Combat & Tactics / Skills & Powers derivative stat.)
    private static string PerceptionEffect(int score)
    {
        var (surprise, detectIn6, noisePct) = PerceptionData(score);
        return $"Surprise: {Fmt(surprise)}, Detect hidden: {detectIn6}-in-6, Hear noise: {noisePct}%";
    }

    private static (int surpriseAdj, int detectHiddenIn6, int hearNoisePercent) PerceptionData(int score)
    {
        return score switch
        {
            1 or 2 or 3    => (-3, 1, 5),
            4 or 5 or 6    => (-2, 1, 10),
            7 or 8 or 9    => (-1, 1, 15),
            10 or 11 or 12 => (0, 1, 20),
            13 or 14 or 15 => (+1, 2, 25),
            16 or 17       => (+2, 3, 30),
            18 or 19       => (+3, 4, 35),
            20             => (+4, 5, 40),
            _              => (+4, 5, 40)
        };
    }

    // ── CHA: Leadership ─────────────────────────────────────────────────────
    // Maximum henchmen, loyalty base, reaction adjustment.
    private static string LeadershipEffect(int score)
    {
        var (hench, loyalty, react) = LeadershipData(score);
        return $"Max henchmen: {hench}, Loyalty: {Fmt(loyalty)}, Reaction: {Fmt(react)}";
    }

    private static (int maxHenchmen, int loyaltyAdj, int reactionAdj) LeadershipData(int score)
    {
        return score switch
        {
            1        => (0,  -8, -7),
            2        => (1,  -7, -6),
            3        => (1,  -6, -5),
            4        => (1,  -5, -4),
            5        => (2,  -4, -3),
            6        => (2,  -3, -2),
            7        => (3,  -2, -1),
            8        => (3,  -1, 0),
            9 or 10 or 11 => (4, 0, 0),
            12 or 13 => (5, 0, +1),
            14       => (6, +1, +2),
            15       => (7, +3, +3),
            16       => (8, +4, +5),
            17       => (10, +6, +6),
            18       => (15, +8, +7),
            19       => (20, +10, +8),
            _        => (25, +12, +9)
        };
    }

    // ── CHA: Appearance ─────────────────────────────────────────────────────
    // Initial NPC reaction adjustment, social encounter bonus.
    private static string AppearanceEffect(int score)
    {
        int react = AppearanceReaction(score);
        return $"NPC reaction: {Fmt(react)}";
    }

    private static int AppearanceReaction(int score)
    {
        return score switch
        {
            1            => -5,
            2 or 3       => -3,
            4 or 5       => -2,
            6 or 7 or 8  => -1,
            _ when score >= 9 && score <= 12 => 0,
            13 or 14 or 15 => +1,
            16 or 17       => +2,
            _              => +3
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string Fmt(int n) => n > 0 ? $"+{n}" : n.ToString();
}

public class SubAbilityMechanicalTotals
{
    // Combat
    public int MeleeAttackBonus { get; set; }
    public int MeleeDamageBonus { get; set; }
    public int MissileAttackBonus { get; set; }
    public int ArmorClassAdjustment { get; set; }
    public int InitiativeAdjustment { get; set; }
    public int DefensiveAdjustment { get; set; }
    public int SurpriseAdjustment { get; set; }
    public int PickPocketsAdjustment { get; set; }
    public int OpenLocksAdjustment { get; set; }
    public int MoveSilentlyAdjustment { get; set; }
    public int ClimbWallsAdjustment { get; set; }

    // Strength/Encumbrance
    public int CarryCapacityLbs { get; set; }
    public int MaxPressLbs { get; set; }
    public int OpenDoorsScore { get; set; }
    public int BendBarsPercent { get; set; }

    // Durability
    public int HpPerLevel { get; set; }
    public int SystemShockPercent { get; set; }
    public int ResurrectionPercent { get; set; }
    public int PoisonSaveAdjustment { get; set; }

    // Arcane/divine learning and control
    public int LearnSpellsPercent { get; set; }
    public int SpellLevelMaximum { get; set; }
    public int MaxSpellsPerLevel { get; set; }
    public int MagicDefenseAdjustment { get; set; }
    public int SpellImmunityPercent { get; set; }
    public string BonusPriestSpells { get; set; } = "none";
    public int PriestSpellFailurePercent { get; set; }

    // Utility/social
    public int BonusNwpSlots { get; set; }
    public int ExtraLanguages { get; set; }
    public int DetectHiddenIn6 { get; set; }
    public int HearNoisePercent { get; set; }
    public int MaxHenchmen { get; set; }
    public int LoyaltyAdjustment { get; set; }
    public int ReactionAdjustment { get; set; }

    public Dictionary<string, int> ToDerivedStats() => new()
    {
        ["melee_attack_bonus"] = MeleeAttackBonus,
        ["melee_damage_bonus"] = MeleeDamageBonus,
        ["missile_attack_bonus"] = MissileAttackBonus,
        ["armor_class_adjustment"] = ArmorClassAdjustment,
        ["initiative_adjustment"] = InitiativeAdjustment,
        ["defensive_adjustment"] = DefensiveAdjustment,
        ["surprise_adjustment"] = SurpriseAdjustment,
        ["pick_pockets_adjustment"] = PickPocketsAdjustment,
        ["open_locks_adjustment"] = OpenLocksAdjustment,
        ["move_silently_adjustment"] = MoveSilentlyAdjustment,
        ["climb_walls_adjustment"] = ClimbWallsAdjustment,
        ["carry_capacity_lbs"] = CarryCapacityLbs,
        ["max_press_lbs"] = MaxPressLbs,
        ["open_doors_score"] = OpenDoorsScore,
        ["bend_bars_percent"] = BendBarsPercent,
        ["hp_per_level"] = HpPerLevel,
        ["system_shock_percent"] = SystemShockPercent,
        ["resurrection_percent"] = ResurrectionPercent,
        ["poison_save_adjustment"] = PoisonSaveAdjustment,
        ["learn_spells_percent"] = LearnSpellsPercent,
        ["spell_level_maximum"] = SpellLevelMaximum,
        ["max_spells_per_level"] = MaxSpellsPerLevel,
        ["magic_defense_adjustment"] = MagicDefenseAdjustment,
        ["spell_immunity_percent"] = SpellImmunityPercent,
        ["priest_spell_failure_percent"] = PriestSpellFailurePercent,
        ["bonus_nwp_slots"] = BonusNwpSlots,
        ["extra_languages"] = ExtraLanguages,
        ["detect_hidden_in_6"] = DetectHiddenIn6,
        ["hear_noise_percent"] = HearNoisePercent,
        ["max_henchmen"] = MaxHenchmen,
        ["loyalty_adjustment"] = LoyaltyAdjustment,
        ["reaction_adjustment"] = ReactionAdjustment,
    };

    public List<string> ToNotes()
    {
        return new List<string>
        {
            $"Strength/Dexterity combat: melee atk {Fmt(MeleeAttackBonus)}, melee dmg {Fmt(MeleeDamageBonus)}, missile atk {Fmt(MissileAttackBonus)}, defense {Fmt(DefensiveAdjustment)}, reaction {Fmt(ReactionAdjustment)}",
            $"Thief operations: pick pockets {Fmt(PickPocketsAdjustment)}, open locks {Fmt(OpenLocksAdjustment)}, move silently {Fmt(MoveSilentlyAdjustment)}, climb walls {Fmt(ClimbWallsAdjustment)}",
            $"Strength operations: carry {CarryCapacityLbs} lbs, max press {MaxPressLbs} lbs, open doors {OpenDoorsScore}, bend bars {BendBarsPercent}%",
            $"Durability: HP/level {Fmt(HpPerLevel)}, system shock {SystemShockPercent}%, resurrection {ResurrectionPercent}%, poison save {Fmt(PoisonSaveAdjustment)}",
            $"Arcane aptitude: spell level {SpellLevelMaximum}, max spells/level {MaxSpellsPerLevel}, learn spells {LearnSpellsPercent}%, spell immunity {SpellImmunityPercent}%",
            $"Divine aptitude: bonus priest spells {BonusPriestSpells}, priest spell failure {PriestSpellFailurePercent}%, magic defense {Fmt(MagicDefenseAdjustment)}",
            $"Utility and social: NWP slots {Fmt(BonusNwpSlots)}, extra languages {ExtraLanguages}, detect hidden {DetectHiddenIn6}-in-6, hear noise {HearNoisePercent}%, max henchmen {MaxHenchmen}, loyalty {Fmt(LoyaltyAdjustment)}, reaction {Fmt(ReactionAdjustment)}",
        };
    }

    private static string Fmt(int n) => n > 0 ? $"+{n}" : n.ToString();
}
