using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

/// <summary>
/// Dialog for adding a spell to a spellbook, with page calculation and validation.
/// </summary>
public class AddSpellToSpellbookDialog : Window
{
    private readonly string? _spellId;
    private readonly string? _spellName;
    private readonly int _spellLevel;
    private readonly List<WizardSpellbook>? _spellbooks;
    private readonly Random _random = new();
    private WizardSpellbook? _selectedBook;
    private int _rolledPages = 0;

    public WizardSpellbook? SelectedBook => _selectedBook;
    public int SelectedPageCount { get; private set; }

    public AddSpellToSpellbookDialog(
        string spellId,
        string spellName,
        int spellLevel,
        List<WizardSpellbook> spellbooks)
    {
        if (spellbooks == null || spellbooks.Count == 0)
        {
            MessageBox.Show(
                "No spellbooks available. Wizards must have at least one spellbook.",
                "No Spellbooks",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            DialogResult = false;
            Close();
            return;
        }

        _spellId = spellId;
        _spellName = spellName;
        _spellLevel = spellLevel;
        _spellbooks = spellbooks;
        _selectedBook = spellbooks.FirstOrDefault();

        Title = $"Add '{spellName}' to Spellbook";
        SetupUI();
    }

    private void SetupUI()
    {
        var mainGrid = new Grid { Margin = new Thickness(20) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var pageRange = SpellbookUtility.GetPageRange(_spellLevel);

        // Title and spell info
        var titleRow = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var title = new TextBlock
        {
            Text = $"Add Spell: {_spellName} (Level {_spellLevel})",
            FontSize = 16,
            FontWeight = FontWeights.Bold
        };
        titleRow.Children.Add(title);

        var rangeText = new TextBlock
        {
            Text = $"Page Requirement: {pageRange.Min}–{pageRange.Max} pages",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128))
        };
        titleRow.Children.Add(rangeText);
        Grid.SetRow(titleRow, 0);
        mainGrid.Children.Add(titleRow);

        // Spellbook selection
        var bookSelectionRow = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        bookSelectionRow.Children.Add(new TextBlock
        {
            Text = "Select Target Spellbook:",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var bookCombo = new ComboBox { Width = 300 };
        foreach (var book in _spellbooks ?? Enumerable.Empty<WizardSpellbook>())
        {
            int availPages = book.GetAvailablePages();
            string displayText = $"{book.Name} ({book.Type}) - {availPages}/{book.CapacityPages} pages";
            bookCombo.Items.Add(new ComboBoxItem
            {
                Content = displayText,
                Tag = book
            });
        }
        bookCombo.SelectedIndex = 0;
        if (bookCombo.SelectedItem is ComboBoxItem firstItem && firstItem.Tag is WizardSpellbook firstBook)
        {
            _selectedBook = firstBook;
        }
        bookSelectionRow.Children.Add(bookCombo);
        Grid.SetRow(bookSelectionRow, 1);
        mainGrid.Children.Add(bookSelectionRow);

        // Define UI elements first so they can be captured in event handlers
        var rolledValue = new TextBlock
        {
            Text = $"{pageRange.Min} pages",
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 14
        };

        var manualInput = new TextBox
        {
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            Text = pageRange.Min.ToString(),
            MaxLength = 2
        };

        var okButton = new Button
        {
            Content = "ADD TO SPELLBOOK",
            Width = 150,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };

        // Book selection event handler
        bookCombo.SelectionChanged += (_, _) =>
        {
            if (bookCombo.SelectedItem is ComboBoxItem item && item.Tag is WizardSpellbook book)
            {
                _selectedBook = book;
                if (book.GetAvailablePages() < pageRange.Min)
                {
                    MessageBox.Show(
                        $"This spellbook doesn't have enough room for this spell (needs {pageRange.Min} pages, has {book.GetAvailablePages()}).",
                        "Not Enough Space",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    okButton.IsEnabled = false;
                }
                else
                {
                    okButton.IsEnabled = true;
                }
            }
        };

        // Manual input event handler
        manualInput.TextChanged += (_, _) =>
        {
            rolledValue.Text = manualInput.Text.Length > 0 ? $"{manualInput.Text} pages" : "- pages";
        };

        // OK button event handler
        okButton.Click += (_, _) =>
        {
            if (ValidateAndConfirm(manualInput.Text))
            {
                DialogResult = true;
                Close();
            }
        };

        // Page calculation method selection
        var methodRow = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        methodRow.Children.Add(new TextBlock
        {
            Text = "Pages Required:",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var rollButton = new Button
        {
            Content = $"ROLL DICE (1d{(pageRange.Max - pageRange.Min + 1)} + {pageRange.Min - 1})",
            Width = 200,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        rollButton.Click += (_, _) =>
        {
            _rolledPages = _random.Next(pageRange.Min, pageRange.Max + 1);
            manualInput.Text = _rolledPages.ToString();
        };

        var manualLabel = new TextBlock
        {
            Text = "Or enter manually:",
            Margin = new Thickness(0, 8, 0, 4)
        };

        var methodStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        methodStack.Children.Add(rollButton);
        methodRow.Children.Add(methodStack);
        methodRow.Children.Add(manualLabel);
        methodRow.Children.Add(manualInput);

        Grid.SetRow(methodRow, 2);
        mainGrid.Children.Add(methodRow);

        // Rolled pages display
        var rolledRow = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var rolledLabel = new TextBlock
        {
            Text = "Pages to Add:",
            FontWeight = FontWeights.Bold
        };
        rolledRow.Children.Add(rolledLabel);
        rolledRow.Children.Add(rolledValue);
        Grid.SetRow(rolledRow, 3);
        mainGrid.Children.Add(rolledRow);

        // Buttons
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "CANCEL",
            Width = 100,
            Padding = new Thickness(10, 8, 10, 8)
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);
        Grid.SetRow(buttonRow, 5);
        mainGrid.Children.Add(buttonRow);

        this.Content = mainGrid;
        this.Width = 500;
        this.Height = 400;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private bool ValidateAndConfirm(string pageText)
    {
        if (_selectedBook == null)
        {
            MessageBox.Show("Please select a spellbook.", "No Spellbook Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(pageText, out int pages))
        {
            MessageBox.Show("Please enter a valid page count.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var error = SpellbookUtility.GetPageValidationError(pages, _spellLevel);
        if (error != null)
        {
            MessageBox.Show(error, "Invalid Page Count", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!SpellbookUtility.CanAddSpellToBook(_selectedBook, pages))
        {
            MessageBox.Show(
                $"This spellbook doesn't have enough room ({_selectedBook.GetAvailablePages()} pages available, but {pages} needed).",
                "Not Enough Space",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        SelectedPageCount = pages;
        return true;
    }
}
