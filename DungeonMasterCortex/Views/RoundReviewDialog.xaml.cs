using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Views;

public partial class RoundReviewDialog : Window
{
    private readonly List<Combatant> _combatants;
    private Dictionary<string, int> _initiativeChanges = new();

    public RoundReviewDialog(int roundNumber, List<Combatant> combatants)
    {
        _combatants = combatants ?? new List<Combatant>();
        InitializeComponent();
        RoundLabel.Text = $"Round {roundNumber} Starting";
        PopulateCombatants();
    }

    private void PopulateCombatants()
    {
        CombatantList.ItemsSource = _combatants;
    }

    private void BtnEditInitiatives_Click(object sender, RoutedEventArgs e)
    {
        // Collect edits from all initiative boxes
        _initiativeChanges.Clear();
        foreach (var item in CombatantList.Items)
        {
            if (item is not Combatant combatant)
                continue;

            var container = CombatantList.ItemContainerGenerator.ContainerFromItem(item);
            if (container is not ListBoxItem listBoxItem)
                continue;

            var textBox = FindInitiativeBox(listBoxItem);
            if (textBox is not null && int.TryParse(textBox.Text.Trim(), out int newInit))
            {
                if (newInit != combatant.Initiative)
                    _initiativeChanges[combatant.CombatantId] = newInit;
            }
        }

        if (_initiativeChanges.Count == 0)
        {
            MessageBox.Show("No changes made.", "Initiatives", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var kvp in _initiativeChanges)
        {
            var combatant = _combatants.Find(c => c.CombatantId == kvp.Key);
            if (combatant is not null)
                combatant.Initiative = kvp.Value;
        }

        MessageBox.Show($"Updated {_initiativeChanges.Count} initiative value(s).", "Initiatives", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnStartRound_Click(object sender, RoutedEventArgs e)
    {
        // Collect any pending initiative changes before closing
        foreach (var item in CombatantList.Items)
        {
            if (item is not Combatant combatant)
                continue;

            var container = CombatantList.ItemContainerGenerator.ContainerFromItem(item);
            if (container is not ListBoxItem listBoxItem)
                continue;

            var textBox = FindInitiativeBox(listBoxItem);
            if (textBox is not null && int.TryParse(textBox.Text.Trim(), out int newInit))
            {
                if (newInit != combatant.Initiative)
                    combatant.Initiative = newInit;
            }
        }

        DialogResult = true;
    }

    private TextBox? FindInitiativeBox(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is TextBox textBox && textBox.Name == "InitiativeBox")
                return textBox;

            var result = FindInitiativeBox(child);
            if (result is not null)
                return result;
        }
        return null;
    }
}
