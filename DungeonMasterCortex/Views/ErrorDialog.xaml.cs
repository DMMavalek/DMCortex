using System.Windows;

namespace DungeonMasterCortex.Views;

public partial class ErrorDialog : Window
{
    public ErrorDialog(string errorMessage)
    {
        InitializeComponent();
        ErrorTextBox.Text = errorMessage;
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ErrorTextBox.Text);
            MessageBox.Show("Error message copied to clipboard!", "Success", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            MessageBox.Show("Failed to copy to clipboard.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
