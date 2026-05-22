using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Views;

public partial class AoeTargetSelectorDialog : Window
{
    private readonly List<Combatant> _availableTargets;
    public List<Combatant> SelectedTargets { get; private set; } = new();

    public AoeTargetSelectorDialog(List<Combatant> availableTargets)
    {
        _availableTargets = availableTargets ?? new List<Combatant>();
        InitializeComponent();
        PopulateTargets();
    }

    private void PopulateTargets()
    {
        TargetList.ItemsSource = _availableTargets;
    }

    private void TargetCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSelectionCount();
    }

    private void TargetCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        int count = 0;
        foreach (var item in TargetList.Items)
        {
            if (item is not Combatant combatant)
                continue;

            var container = TargetList.ItemContainerGenerator.ContainerFromItem(item);
            if (container is not ListBoxItem listBoxItem)
                continue;

            var checkbox = FindCheckBox(listBoxItem);
            if (checkbox?.IsChecked == true)
                count++;
        }

        SelectionCountLabel.Text = count == 1 ? "1 target selected" : $"{count} targets selected";
    }

    private CheckBox? FindCheckBox(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is CheckBox checkBox)
                return checkBox;

            var result = FindCheckBox(child);
            if (result is not null)
                return result;
        }
        return null;
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in TargetList.Items)
        {
            var container = TargetList.ItemContainerGenerator.ContainerFromItem(item);
            if (container is ListBoxItem listBoxItem)
            {
                var checkbox = FindCheckBox(listBoxItem);
                if (checkbox is not null)
                    checkbox.IsChecked = true;
            }
        }
        UpdateSelectionCount();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in TargetList.Items)
        {
            var container = TargetList.ItemContainerGenerator.ContainerFromItem(item);
            if (container is ListBoxItem listBoxItem)
            {
                var checkbox = FindCheckBox(listBoxItem);
                if (checkbox is not null)
                    checkbox.IsChecked = false;
            }
        }
        UpdateSelectionCount();
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        SelectedTargets.Clear();
        foreach (var item in TargetList.Items)
        {
            if (item is not Combatant combatant)
                continue;

            var container = TargetList.ItemContainerGenerator.ContainerFromItem(item);
            if (container is not ListBoxItem listBoxItem)
                continue;

            var checkbox = FindCheckBox(listBoxItem);
            if (checkbox?.IsChecked == true)
                SelectedTargets.Add(combatant);
        }

        DialogResult = true;
    }
}
