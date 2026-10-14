using System;
using System.Collections.Generic;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DungeonMasterCortex.Services;

public static class BlankCharacterSheetPdfService
{
    private const double PageWidth = 612;
    private const double PageHeight = 792;
    private const double Margin = 28;
    private const double ContentWidth = PageWidth - (Margin * 2);
    private const double SectionHeaderHeight = 11;
    private static bool _fontSettingsInitialized;

    private static readonly XColor SheetRed = XColor.FromArgb(221, 105, 109);
    private static readonly XColor SheetRedLight = XColor.FromArgb(235, 151, 154);
    private static readonly XColor Ink = XColors.Black;
    private static readonly XPen GridPen = new(Ink, 0.75);
    private static XFont LabelFont { get; set; } = null!;
    private static XFont LabelBoldFont { get; set; } = null!;
    private static XFont HeaderFont { get; set; } = null!;

    public static void Create(string filePath)
    {
        EnsureFontSettings();

        using var document = new PdfDocument();
        document.Info.Title = "Mapped Blank Fillable Character Sheet";
        document.Info.Subject = "Dungeon Master Cortex four-page mapped character sheet";
        document.Info.Creator = "Dungeon Master Cortex";
        ConfigureAcroForm(document);

        AddCharacterPage(document);
        AddInventoryPage(document);
        AddSpellPage(document, 3, 52, 0);
        AddSpellPage(document, 4, 60, 52);
        document.Save(filePath);
    }

    private static void EnsureFontSettings()
    {
        if (_fontSettingsInitialized)
            return;

        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        LabelFont = new XFont("Arial", 6.5);
        LabelBoldFont = new XFont("Arial", 6.5, XFontStyleEx.Bold);
        HeaderFont = new XFont("Arial", 7, XFontStyleEx.Bold);
        _fontSettingsInitialized = true;
    }

    private static void ConfigureAcroForm(PdfDocument document)
    {
        var acroForm = new PdfDictionary(document);
        acroForm.Elements.SetString("/DMCortexDocumentType", "dmcortex.character-sheet");
        acroForm.Elements.SetString("/DMCortexSchemaVersion", "1");
        acroForm.Elements.SetBoolean("/NeedAppearances", true);
        acroForm.Elements.SetString("/DA", "/Helv 7 Tf 0 g");
        acroForm.Elements["/Fields"] = new PdfArray(document);
        document.Internals.AddObject(acroForm);
        document.Internals.Catalog.Elements["/AcroForm"] = acroForm.ReferenceNotNull;

        var font = new PdfDictionary(document);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        document.Internals.AddObject(font);

        var fonts = new PdfDictionary(document);
        fonts.Elements["/Helv"] = font.ReferenceNotNull;
        var resources = new PdfDictionary(document);
        resources.Elements["/Font"] = fonts;
        acroForm.Elements["/DR"] = resources;
    }

    private static void AddCharacterPage(PdfDocument document)
    {
        PdfPage page = AddPage(document);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        DrawPageBackground(graphics);

        DrawPersonalInformation(document, page, graphics, Margin, 28, ContentWidth, 76);
        DrawAbilityScores(document, page, graphics, Margin, 116, ContentWidth, 140);

        double middleTop = 268;
        DrawListSection(document, page, graphics, "Saving Throws", Margin, middleTop, 86, 160, 10, "saving_throws", true,
        ["Paralyzation", "Poison", "Death Magic", "Petrification", "Polymorph", "Rod", "Staff", "Wand", "Breath Weapon", "Spell"]);
        DrawListSection(document, page, graphics, "Combat", Margin + 98, middleTop, 90, 160, 8, "combat", true,
        ["Hit Points", "Base THAC0", "Melee THAC0", "Missile THAC0", "Armor Class", "Natural AC", "Balance Adj", "FINAL AC"]);
        DrawListSection(document, page, graphics, "Weapon Proficiencies", Margin + 200, middleTop, 158, 160, 10, "proficiencies.weapon", false, null);
        DrawListSection(document, page, graphics, "Non-Weapon Proficiencies", Margin + 370, middleTop, 186, 160, 10, "proficiencies.nonweapon", false, null);

        DrawWeapons(document, page, graphics, Margin, 440, ContentWidth, 224);
        DrawListSection(document, page, graphics, "Racial Abilities", Margin, 676, 270, 108, 8, "abilities.racial", false, null);
        DrawListSection(document, page, graphics, "Class Abilities", Margin + 286, 676, 270, 108, 8, "abilities.class", false, null);
    }

    private static void DrawPersonalInformation(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height)
    {
        DrawSectionFrame(graphics, "Personal Information", left, top, width, height);
        double bodyTop = top + SectionHeaderHeight + 2;
        double rowHeight = 11;

        DrawPersonalColumn(document, page, graphics, left + 2, bodyTop, 198, rowHeight,
        [
            Map("Name", "identity.name"), Map("Race", "identity.race"), Map("Class", "identity.class"),
            Map("XP", "identity.experience"), Map("Next Level", "identity.next_level_experience"),
        ]);
        DrawPersonalColumn(document, page, graphics, left + 218, bodyTop, 190, rowHeight,
        [
            Map("Player", "identity.player"), Map("Gender", "identity.gender"), Map("Level", "identity.level"),
            Map("Alignment", "identity.alignment"), Map("Kit", "identity.kit"),
        ]);
        DrawPersonalColumn(document, page, graphics, left + 424, bodyTop, 128, rowHeight,
        [
            Map("Height", "identity.height"), Map("Weight", "identity.weight"),
            Map("Age", "identity.age"), Map("CPs", "identity.character_points"),
        ]);
    }

    private static void DrawPersonalColumn(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double rowHeight, IReadOnlyList<Mapping> rows)
    {
        double labelWidth = Math.Min(66, width * 0.35);
        for (int index = 0; index < rows.Count; index++)
        {
            double y = top + (index * rowHeight);
            graphics.DrawString(rows[index].Label, LabelFont, XBrushes.Black, left, y + 7);
            graphics.DrawLine(GridPen, left + labelWidth, y + rowHeight - 2, left + width, y + rowHeight - 2);
            AddTextField(document, page, rows[index].Code, left + labelWidth + 1, y, width - labelWidth - 2, rowHeight - 2, false, 7);
        }
    }

    private static void DrawAbilityScores(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height)
    {
        DrawSectionFrame(graphics, "Ability Scores", left, top, width, height);
        double rowTop = top + SectionHeaderHeight;
        double rowHeight = (height - SectionHeaderHeight) / 6;

        DrawAbilityBand(document, page, graphics, left, rowTop + (rowHeight * 0), width, rowHeight, "STR", "abilities.strength",
            [Map("Stamina", "subabilities.strength.stamina"), Map("Weight Allowance", "ability_effects.strength.weight_allowance"), Map("Muscle", "subabilities.strength.muscle"), Map("Att Adj", "ability_effects.strength.attack_adjustment")],
            [Map("Dmg Adj", "ability_effects.strength.damage_adjustment"), Map("Max Press", "ability_effects.strength.maximum_press"), Map("Open Doors", "ability_effects.strength.open_doors"), Map("Bend Bar", "ability_effects.strength.bend_bars")]);
        DrawAbilityBand(document, page, graphics, left, rowTop + (rowHeight * 1), width, rowHeight, "DEX", "abilities.dexterity",
            [Map("Aim", "subabilities.dexterity.aim"), Map("Missile Adj", "ability_effects.dexterity.missile_adjustment"), Map("Pick Pockets", "rogue_abilities.pick_pockets"), Map("Open Locks", "rogue_abilities.open_locks")],
            [Map("Balance", "subabilities.dexterity.balance"), Map("Reaction Adj", "ability_effects.dexterity.reaction_adjustment"), Map("Armor Class", "ability_effects.dexterity.armor_class"), Map("Move Silently", "rogue_abilities.move_silently"), Map("Climb Walls", "rogue_abilities.climb_walls")]);
        DrawAbilityBand(document, page, graphics, left, rowTop + (rowHeight * 2), width, rowHeight, "CON", "abilities.constitution",
            [Map("Health", "subabilities.constitution.health"), Map("System Shock", "ability_effects.constitution.system_shock"), Map("Poison Save", "ability_effects.constitution.poison_save")],
            [Map("Fitness", "subabilities.constitution.fitness"), Map("Hit Point Adj", "ability_effects.constitution.hit_point_adjustment"), Map("Ress Chance", "ability_effects.constitution.resurrection_chance")]);
        DrawAbilityBand(document, page, graphics, left, rowTop + (rowHeight * 3), width, rowHeight, "INT", "abilities.intelligence",
            [Map("Reason", "subabilities.intelligence.reason"), Map("Max Spells per Level", "ability_effects.intelligence.maximum_spells_per_level"), Map("Illusion Immunity", "ability_effects.intelligence.illusion_immunity")],
            [Map("Knowledge", "subabilities.intelligence.knowledge"), Map("Bonus Proficiencies", "ability_effects.intelligence.bonus_proficiencies"), Map("Learn New Spell", "ability_effects.intelligence.learn_new_spell")]);
        DrawAbilityBand(document, page, graphics, left, rowTop + (rowHeight * 4), width, rowHeight, "WIS", "abilities.wisdom",
            [Map("Intuition", "subabilities.wisdom.intuition"), Map("1st Bonus", "ability_effects.wisdom.bonus_clerical_spells.level_1"), Map("2nd", "ability_effects.wisdom.bonus_clerical_spells.level_2"), Map("3rd", "ability_effects.wisdom.bonus_clerical_spells.level_3"), Map("4th", "ability_effects.wisdom.bonus_clerical_spells.level_4"), Map("5th", "ability_effects.wisdom.bonus_clerical_spells.level_5"), Map("Spell Failure", "ability_effects.wisdom.spell_failure")],
            [Map("Willpower", "subabilities.wisdom.willpower"), Map("Magic Defense Adj", "ability_effects.wisdom.magic_defense_adjustment"), Map("Spell Immunity", "ability_effects.wisdom.spell_immunity")]);
        DrawAbilityBand(document, page, graphics, left, rowTop + (rowHeight * 5), width, rowHeight, "CHA", "abilities.charisma",
            [Map("Leadership", "subabilities.charisma.leadership"), Map("Loyalty Base", "ability_effects.charisma.loyalty_base"), Map("Max Henchmen", "ability_effects.charisma.maximum_henchmen")],
            [Map("Appearance", "subabilities.charisma.appearance"), Map("Initial Reaction Adj", "ability_effects.charisma.initial_reaction_adjustment")]);
    }

    private static void DrawAbilityBand(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height, string abbreviation, string scoreCode, IReadOnlyList<Mapping> firstRow, IReadOnlyList<Mapping> secondRow)
    {
        const double abbreviationWidth = 43;
        const double scoreWidth = 43;
        double halfHeight = height / 2;
        graphics.DrawRectangle(GridPen, left, top, width, height);
        graphics.DrawLine(GridPen, left + abbreviationWidth, top, left + abbreviationWidth, top + height);
        graphics.DrawLine(GridPen, left + abbreviationWidth + scoreWidth, top, left + abbreviationWidth + scoreWidth, top + height);
        graphics.DrawString(abbreviation, LabelBoldFont, XBrushes.Black, left + 2, top + 8);
        AddTextField(document, page, scoreCode, left + abbreviationWidth + 1, top + 1, scoreWidth - 2, height - 2, false, 7);

        double effectsLeft = left + abbreviationWidth + scoreWidth;
        DrawMappingRow(document, page, graphics, effectsLeft, top, width - abbreviationWidth - scoreWidth, halfHeight, firstRow);
        DrawMappingRow(document, page, graphics, effectsLeft, top + halfHeight, width - abbreviationWidth - scoreWidth, halfHeight, secondRow);
    }

    private static void DrawMappingRow(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height, IReadOnlyList<Mapping> mappings)
    {
        if (mappings.Count == 0)
            return;

        double cellWidth = width / mappings.Count;
        for (int index = 0; index < mappings.Count; index++)
        {
            double x = left + (index * cellWidth);
            double labelWidth = cellWidth * 0.68;
            graphics.DrawRectangle(GridPen, x, top, cellWidth, height);
            graphics.DrawLine(GridPen, x + labelWidth, top, x + labelWidth, top + height);
            graphics.DrawString(mappings[index].Label, LabelFont, XBrushes.Black, x + 2, top + 7);
            AddTextField(document, page, mappings[index].Code, x + labelWidth + 1, top + 1, cellWidth - labelWidth - 2, height - 2, false, 6);
        }
    }

    private static void DrawListSection(PdfDocument document, PdfPage page, XGraphics graphics, string title, double left, double top, double width, double height, int rowCount, string codePrefix, bool hasLabels, IReadOnlyList<string>? labels)
    {
        DrawSectionFrame(graphics, title, left, top, width, height);
        double rowHeight = (height - SectionHeaderHeight) / rowCount;
        double labelWidth = hasLabels ? width * 0.74 : 0;
        for (int index = 0; index < rowCount; index++)
        {
            double y = top + SectionHeaderHeight + (index * rowHeight);
            graphics.DrawRectangle(GridPen, left, y, width, rowHeight);
            if (hasLabels)
            {
                graphics.DrawLine(GridPen, left + labelWidth, y, left + labelWidth, y + rowHeight);
                string label = labels is not null && index < labels.Count ? labels[index] : string.Empty;
                graphics.DrawString(label, LabelFont, XBrushes.Black, left + 2, y + Math.Min(8, rowHeight - 2));
                AddTextField(document, page, $"{codePrefix}.{NormalizeCode(label)}", left + labelWidth + 1, y + 1, width - labelWidth - 2, rowHeight - 2, false, 6);
            }
            else
            {
                AddTextField(document, page, $"{codePrefix}.{index:00}", left + 1, y + 1, width - 2, rowHeight - 2, false, 6);
            }
        }
    }

    private static void DrawWeapons(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height)
    {
        DrawSectionFrame(graphics, "Weapons", left, top, width, height);
        double y = top + SectionHeaderHeight;
        y = DrawWeaponTable(document, page, graphics, left, y, width, "Main Hand", "weapons.main", 8);
        DrawWeaponTable(document, page, graphics, left, y, width, "Off Hand", "weapons.off_hand", 6);
    }

    private static double DrawWeaponTable(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, string caption, string codePrefix, int rowCount)
    {
        const double captionHeight = 9;
        const double headerHeight = 20;
        const double rowHeight = 10;
        string[] headers = ["Weapon", "Melee", "Missile", "Attacks /\nRound", "Speed\nFactor", "Sm-Med", "Large", "Short", "Medium", "Long", "Extended"];
        string[] codes = ["name", "thac0_melee", "thac0_missile", "attacks_per_round", "speed_factor", "damage_small_medium", "damage_large", "range_short", "range_medium", "range_long", "range_extended"];
        double[] weights = [112, 44, 44, 42, 44, 44, 44, 42, 42, 42, 44];
        double weightTotal = 0;
        foreach (double weight in weights) weightTotal += weight;

        graphics.DrawRectangle(new XSolidBrush(SheetRedLight), left, top, width, captionHeight);
        graphics.DrawRectangle(GridPen, left, top, width, captionHeight);
        graphics.DrawString(caption, LabelBoldFont, XBrushes.Black, left + 2, top + 7);
        double headerTop = top + captionHeight;
        double x = left;
        var widths = new double[weights.Length];
        for (int column = 0; column < weights.Length; column++)
        {
            widths[column] = width * weights[column] / weightTotal;
            graphics.DrawRectangle(GridPen, x, headerTop, widths[column], headerHeight);
            DrawCenteredText(graphics, headers[column], x, headerTop, widths[column], headerHeight, LabelBoldFont);
            x += widths[column];
        }

        for (int row = 0; row < rowCount; row++)
        {
            x = left;
            double rowTop = headerTop + headerHeight + (row * rowHeight);
            for (int column = 0; column < widths.Length; column++)
            {
                graphics.DrawRectangle(GridPen, x, rowTop, widths[column], rowHeight);
                AddTextField(document, page, $"{codePrefix}.{row:00}.{codes[column]}", x + 1, rowTop + 1, widths[column] - 2, rowHeight - 2, false, 5.5);
                x += widths[column];
            }
        }

        return headerTop + headerHeight + (rowCount * rowHeight);
    }

    private static void AddInventoryPage(PdfDocument document)
    {
        PdfPage page = AddPage(document);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        DrawPageBackground(graphics);

        DrawPairedList(document, page, graphics, "Traits", Margin, 28, 270, 114, "traits", 8);
        DrawPairedList(document, page, graphics, "Disadvantages", Margin + 286, 28, 270, 114, "disadvantages", 8);
        DrawInventory(document, page, graphics, Margin, 154, ContentWidth, 158);
        DrawMovement(document, page, graphics, Margin, 324, ContentWidth, 96);
    }

    private static void DrawPairedList(PdfDocument document, PdfPage page, XGraphics graphics, string title, double left, double top, double width, double height, string codePrefix, int rowCount)
    {
        DrawSectionFrame(graphics, title, left, top, width, height);
        double rowHeight = (height - SectionHeaderHeight) / rowCount;
        double pointsWidth = 46;
        for (int row = 0; row < rowCount; row++)
        {
            double y = top + SectionHeaderHeight + (row * rowHeight);
            graphics.DrawRectangle(GridPen, left, y, width, rowHeight);
            graphics.DrawLine(GridPen, left + width - pointsWidth, y, left + width - pointsWidth, y + rowHeight);
            AddTextField(document, page, $"{codePrefix}.{row:00}.name", left + 1, y + 1, width - pointsWidth - 2, rowHeight - 2, false, 6);
            AddTextField(document, page, $"{codePrefix}.{row:00}.points", left + width - pointsWidth + 1, y + 1, pointsWidth - 2, rowHeight - 2, false, 6);
        }
    }

    private static void DrawInventory(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height)
    {
        DrawSectionFrame(graphics, "Inventory", left, top, width, height);
        const int groupCount = 3;
        const int rowCount = 12;
        double groupWidth = width / groupCount;
        double headerTop = top + SectionHeaderHeight;
        double tableHeight = height - SectionHeaderHeight;
        double headerHeight = 12;
        double rowHeight = (tableHeight - headerHeight) / rowCount;

        for (int group = 0; group < groupCount; group++)
        {
            double x = left + (group * groupWidth);
            double quantityWidth = 28;
            graphics.DrawRectangle(GridPen, x, headerTop, groupWidth, headerHeight);
            graphics.DrawLine(GridPen, x + quantityWidth, headerTop, x + quantityWidth, headerTop + tableHeight);
            graphics.DrawString("#", LabelBoldFont, XBrushes.Black, x + 10, headerTop + 8);
            graphics.DrawString("Item", LabelBoldFont, XBrushes.Black, x + quantityWidth + 3, headerTop + 8);
            for (int row = 0; row < rowCount; row++)
            {
                double y = headerTop + headerHeight + (row * rowHeight);
                graphics.DrawRectangle(GridPen, x, y, groupWidth, rowHeight);
                int itemIndex = (group * rowCount) + row;
                AddTextField(document, page, $"inventory.{itemIndex:00}.quantity", x + 1, y + 1, quantityWidth - 2, rowHeight - 2, false, 6);
                AddTextField(document, page, $"inventory.{itemIndex:00}.item", x + quantityWidth + 1, y + 1, groupWidth - quantityWidth - 2, rowHeight - 2, false, 6);
            }
        }
    }

    private static void DrawMovement(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height)
    {
        DrawSectionFrame(graphics, "Movement and Encumbrance", left, top, width, height);
        string[] columns = ["Encumbrance", "Unencumbered", "Light", "Moderate", "Heavy", "Severe"];
        string[] rowLabels = ["Weight (lbs)", "Movement", "THAC0", "Armor Class"];
        string[] rowCodes = ["weight", "movement", "thac0", "armor_class"];
        double labelWidth = 116;
        double dataWidth = (width - labelWidth) / 5;
        double headerTop = top + SectionHeaderHeight;
        double rowHeight = 12;

        graphics.DrawRectangle(GridPen, left, headerTop, labelWidth, rowHeight);
        graphics.DrawString(columns[0], LabelBoldFont, XBrushes.Black, left + 2, headerTop + 8);
        for (int column = 1; column < columns.Length; column++)
        {
            double x = left + labelWidth + ((column - 1) * dataWidth);
            graphics.DrawRectangle(GridPen, x, headerTop, dataWidth, rowHeight);
            DrawCenteredText(graphics, columns[column], x, headerTop, dataWidth, rowHeight, LabelBoldFont);
        }

        for (int row = 0; row < rowLabels.Length; row++)
        {
            double y = headerTop + rowHeight + (row * rowHeight);
            graphics.DrawRectangle(GridPen, left, y, labelWidth, rowHeight);
            graphics.DrawString(rowLabels[row], LabelFont, XBrushes.Black, left + 2, y + 8);
            for (int column = 0; column < 5; column++)
            {
                double x = left + labelWidth + (column * dataWidth);
                graphics.DrawRectangle(GridPen, x, y, dataWidth, rowHeight);
                string category = NormalizeCode(columns[column + 1]);
                AddTextField(document, page, $"encumbrance.{category}.{rowCodes[row]}", x + 1, y + 1, dataWidth - 2, rowHeight - 2, false, 6);
            }
        }

        double summaryTop = headerTop + (rowHeight * 5) + 3;
        graphics.DrawString("Currently carrying", LabelFont, XBrushes.Black, left + 2, summaryTop + 8);
        AddTextField(document, page, "encumbrance.current_weight", left + 84, summaryTop, 58, 11, false, 6);
        graphics.DrawString("Pounds", LabelFont, XBrushes.Black, left + 145, summaryTop + 8);
        graphics.DrawString("Current Enc:", LabelFont, XBrushes.Black, left + 190, summaryTop + 8);
        AddTextField(document, page, "encumbrance.current_category", left + 252, summaryTop, 92, 11, false, 6);
        graphics.DrawString("Movement", LabelFont, XBrushes.Black, left + 356, summaryTop + 8);
        AddTextField(document, page, "encumbrance.current_movement", left + 405, summaryTop, 70, 11, false, 6);
    }

    private static void AddSpellPage(PdfDocument document, int pageNumber, int rowCount, int rowOffset)
    {
        PdfPage page = AddPage(document);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        DrawPageBackground(graphics);

        if (pageNumber == 3)
        {
            DrawSpellSummary(document, page, graphics, Margin, 28, ContentWidth, 92);
            DrawSpellGrid(document, page, graphics, Margin, 132, ContentWidth, 648, rowCount, rowOffset);
        }
        else
        {
            DrawSpellGrid(document, page, graphics, Margin, 28, ContentWidth, 752, rowCount, rowOffset);
        }
    }

    private static void DrawSpellSummary(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height)
    {
        DrawSectionFrame(graphics, "Spells", left, top, width, height);
        double rowTop = top + SectionHeaderHeight;
        double labelWidth = 44;
        double classWidth = 110;
        double levelWidth = (width - labelWidth - classWidth) / 9;
        graphics.DrawRectangle(GridPen, left, rowTop, labelWidth, 13);
        graphics.DrawString("Level:", LabelFont, XBrushes.Black, left + 2, rowTop + 8);
        AddTextField(document, page, "spells.character_level", left + labelWidth + 1, rowTop + 1, classWidth - 2, 11, false, 6);
        for (int level = 1; level <= 9; level++)
        {
            double x = left + labelWidth + classWidth + ((level - 1) * levelWidth);
            graphics.DrawRectangle(GridPen, x, rowTop, levelWidth, 13);
            DrawCenteredText(graphics, Ordinal(level), x, rowTop, levelWidth, 13, LabelFont);
            AddTextField(document, page, $"spells.slots.level_{level}", x + 1, rowTop + 1, levelWidth - 2, 11, false, 6);
        }

        double classTop = rowTop + 13;
        graphics.DrawRectangle(GridPen, left, classTop, labelWidth, 13);
        graphics.DrawString("Class:", LabelFont, XBrushes.Black, left + 2, classTop + 8);
        AddTextField(document, page, "spells.class", left + labelWidth + 1, classTop + 1, classWidth - 2, 11, false, 6);

        Mapping[] settings =
        [
            Map("Clerical Spell Failure Rate:", "spells.clerical_failure_rate"),
            Map("Wizard Max Spells Per Level:", "spells.wizard_maximum_per_level"),
            Map("Wizard Chance to Learn New Spell:", "spells.wizard_learn_chance"),
            Map("Maximum Wizard Spell Level:", "spells.wizard_maximum_level"),
        ];
        double settingsTop = classTop + 25;
        for (int index = 0; index < settings.Length; index++)
        {
            double y = settingsTop + (index * 11);
            graphics.DrawRectangle(GridPen, left, y, 260, 11);
            graphics.DrawString(settings[index].Label, LabelFont, XBrushes.Black, left + 2, y + 8);
            AddTextField(document, page, settings[index].Code, left + 176, y + 1, 83, 9, false, 6);
        }
    }

    private static void DrawSpellGrid(PdfDocument document, PdfPage page, XGraphics graphics, double left, double top, double width, double height, int rowCount, int rowOffset)
    {
        DrawSectionFrame(graphics, "Spells Known", left, top, width, height);
        string[] headers = ["Spell\nLevel", "Spell Name", "Casting\nTime", "Range", "Area of\nEffect", "Component", "Duration"];
        string[] codes = ["level", "name", "casting_time", "range", "area_of_effect", "components", "duration"];
        double[] weights = [44, 195, 44, 88, 66, 66, 62];
        double totalWeight = 0;
        foreach (double weight in weights) totalWeight += weight;
        var widths = new double[weights.Length];
        double headerTop = top + SectionHeaderHeight;
        const double headerHeight = 24;
        double rowHeight = (height - SectionHeaderHeight - headerHeight) / rowCount;
        double x = left;
        for (int column = 0; column < weights.Length; column++)
        {
            widths[column] = width * weights[column] / totalWeight;
            graphics.DrawRectangle(GridPen, x, headerTop, widths[column], headerHeight);
            DrawCenteredText(graphics, headers[column], x, headerTop, widths[column], headerHeight, LabelBoldFont);
            x += widths[column];
        }

        for (int row = 0; row < rowCount; row++)
        {
            x = left;
            double y = headerTop + headerHeight + (row * rowHeight);
            int mappedIndex = rowOffset + row;
            for (int column = 0; column < widths.Length; column++)
            {
                graphics.DrawRectangle(GridPen, x, y, widths[column], rowHeight);
                AddTextField(document, page, $"spells.known.{mappedIndex:000}.{codes[column]}", x + 1, y + 1, widths[column] - 2, rowHeight - 2, false, 5.5);
                x += widths[column];
            }
        }
    }

    private static PdfPage AddPage(PdfDocument document)
    {
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromPoint(PageWidth);
        page.Height = XUnit.FromPoint(PageHeight);
        return page;
    }

    private static void DrawPageBackground(XGraphics graphics)
    {
        graphics.DrawRectangle(XBrushes.White, 0, 0, PageWidth, PageHeight);
    }

    private static void DrawSectionFrame(XGraphics graphics, string title, double left, double top, double width, double height)
    {
        graphics.DrawRectangle(XBrushes.White, left, top, width, height);
        graphics.DrawRectangle(GridPen, left, top, width, height);
        graphics.DrawRectangle(new XSolidBrush(SheetRed), left, top, width, SectionHeaderHeight);
        graphics.DrawRectangle(GridPen, left, top, width, SectionHeaderHeight);
        DrawCenteredText(graphics, title, left, top, width, SectionHeaderHeight, HeaderFont);
    }

    private static void DrawCenteredText(XGraphics graphics, string text, double left, double top, double width, double height, XFont font)
    {
        string[] lines = text.Split('\n');
        double lineHeight = font.Size + 1;
        double startY = top + ((height - (lines.Length * lineHeight)) / 2);
        for (int index = 0; index < lines.Length; index++)
        {
            graphics.DrawString(lines[index], font, XBrushes.Black, new XRect(left, startY + (index * lineHeight), width, lineHeight), XStringFormats.Center);
        }
    }

    private static void AddTextField(PdfDocument document, PdfPage page, string name, double left, double top, double width, double height, bool multiLine, double fontSize)
    {
        if (width <= 1 || height <= 1)
            return;

        var widget = new PdfDictionary(document);
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements.SetName("/FT", "/Tx");
        widget.Elements.SetString("/T", name);
        widget.Elements.SetString("/V", string.Empty);
        widget.Elements.SetString("/DA", $"/Helv {fontSize:0.#} Tf 0 g");
        widget.Elements.SetInteger("/F", 4);
        widget.Elements.SetInteger("/Ff", multiLine ? 4096 : 0);
        widget.Elements.SetRectangle("/Rect", new PdfRectangle(new XRect(left, PageHeight - top - height, width, height)));

        var borderStyle = new PdfDictionary(document);
        borderStyle.Elements.SetInteger("/W", 0);
        borderStyle.Elements.SetName("/S", "/S");
        widget.Elements["/BS"] = borderStyle;
        document.Internals.AddObject(widget);

        PdfArray? annotations = page.Elements.GetArray("/Annots");
        if (annotations is null)
        {
            annotations = new PdfArray(document);
            page.Elements["/Annots"] = annotations;
        }
        annotations.Elements.Add(widget.ReferenceNotNull);

        PdfDictionary acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm")
            ?? throw new InvalidOperationException("PDF AcroForm dictionary was not initialized.");
        PdfArray fields = acroForm.Elements.GetArray("/Fields")
            ?? throw new InvalidOperationException("PDF AcroForm fields array was not initialized.");
        fields.Elements.Add(widget.ReferenceNotNull);
    }

    private static string NormalizeCode(string value)
        => value.Trim().ToLowerInvariant()
            .Replace("/", "_")
            .Replace("-", "_")
            .Replace(" ", "_")
            .Replace(":", string.Empty);

    private static string Ordinal(int value) => value switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{value}th",
    };

    private static Mapping Map(string label, string code) => new(label, code);
    private sealed record Mapping(string Label, string Code);
}
