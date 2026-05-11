using System.Windows;

namespace DungeonMasterCortex.Views
{
    public partial class SpellDetailPopup : Window
    {
        public SpellDetailPopup(string detailText)
        {
            InitializeComponent();
            DetailText.Text = detailText;
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.Close();
        }
    }
}
