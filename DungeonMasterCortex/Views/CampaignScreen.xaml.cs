using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Views;

public partial class CampaignScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private int _selectedNpcIdx      = -1;
    private int _selectedLocationIdx = -1;

    public CampaignScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
        ShowCampaignTab("log");
    }

    public void OnEnter()
    {
        _app.SetBanner("Campaign  ›  Session Log");
        ShowCampaignTab("log");
    }

    private void BtnHub_Click(object sender, RoutedEventArgs e) =>
        _app.GoTo("hub", -1);

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void CampTabLog_Click(object sender, RoutedEventArgs e)       => ShowCampaignTab("log");
    private void CampTabNpcs_Click(object sender, RoutedEventArgs e)      => ShowCampaignTab("npcs");
    private void CampTabLocations_Click(object sender, RoutedEventArgs e) => ShowCampaignTab("locations");

    private void ShowCampaignTab(string tab)
    {
        CampTabLog.Visibility       = tab == "log"       ? Visibility.Visible : Visibility.Collapsed;
        CampTabNpcs.Visibility      = tab == "npcs"      ? Visibility.Visible : Visibility.Collapsed;
        CampTabLocations.Visibility = tab == "locations" ? Visibility.Visible : Visibility.Collapsed;

        var active   = (System.Windows.Media.Brush)FindResource("BrushBtnAct");
        var inactive = (System.Windows.Media.Brush)FindResource("BrushBtn");
        CampTabBtnLog.Background       = tab == "log"       ? active : inactive;
        CampTabBtnNpcs.Background      = tab == "npcs"      ? active : inactive;
        CampTabBtnLocations.Background = tab == "locations" ? active : inactive;

        if (tab == "log")       RefreshList();
        if (tab == "npcs")      RefreshNpcList();
        if (tab == "locations") RefreshLocationList();
    }

    // ── Session Log ───────────────────────────────────────────────────────────

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateCampaignEntryInCurrentLicense())
            return;

        var sid   = TxtSessionId.Text.Trim();
        var title = TxtTitle.Text.Trim();
        var body  = TxtBody.Text.Trim();

        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(title))
        {
            MessageBox.Show("Session ID and Title are required.", "Missing Fields",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _app.Campaign.AddEntry(new CampaignEntry { SessionId = sid, Title = title, Body = body });
        TxtSessionId.Text = TxtTitle.Text = TxtBody.Text = "";
        RefreshList();
    }

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = EntryList.SelectedIndex;
        if (idx < 0) return;
        var entry = _app.Campaign.Entries[idx];
        TxtDetail.Text = $"[{entry.SessionId}]  {entry.Title}\n\n{entry.Body}";
    }

    private void RefreshList()
    {
        EntryList.Items.Clear();
        foreach (var entry in _app.Campaign.Entries)
            EntryList.Items.Add(entry.ListDisplay);
    }

    // ── NPCs tab ──────────────────────────────────────────────────────────────

    private void RefreshNpcList()
    {
        NpcList.Items.Clear();
        foreach (var n in _app.Campaign.Npcs)
            NpcList.Items.Add(n.ListDisplay);
        _selectedNpcIdx = -1;
        ClearNpcEditor();
    }

    private void NpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = NpcList.SelectedIndex;
        if (idx < 0 || idx >= _app.Campaign.Npcs.Count) return;
        _selectedNpcIdx = idx;
        var n = _app.Campaign.Npcs[idx];
        NpcName.Text  = n.Name;
        NpcRole.Text  = n.Role;
        NpcNotes.Text = n.Notes;
    }

    private void BtnNewNpc_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateCampaignEntryInCurrentLicense())
            return;

        _app.Campaign.Npcs.Add(new NpcEntry { Name = "New NPC" });
        RefreshNpcList();
        NpcList.SelectedIndex = _app.Campaign.Npcs.Count - 1;
    }

    private void BtnDeleteNpc_Click(object sender, RoutedEventArgs e)
    {
        int idx = NpcList.SelectedIndex;
        if (idx < 0) return;
        var result = MessageBox.Show("Delete this NPC?", "Confirm",
                                     MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _app.Campaign.Npcs.RemoveAt(idx);
        RefreshNpcList();
    }

    private void BtnSaveNpc_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNpcIdx < 0 || _selectedNpcIdx >= _app.Campaign.Npcs.Count) return;
        var n = _app.Campaign.Npcs[_selectedNpcIdx];
        n.Name  = NpcName.Text.Trim();
        n.Role  = NpcRole.Text.Trim();
        n.Notes = NpcNotes.Text.Trim();
        RefreshNpcList();
        NpcList.SelectedIndex = _selectedNpcIdx;
    }

    private void ClearNpcEditor()
    {
        NpcName.Text = NpcRole.Text = NpcNotes.Text = "";
    }

    // ── Locations tab ─────────────────────────────────────────────────────────

    private void RefreshLocationList()
    {
        LocationList.Items.Clear();
        foreach (var l in _app.Campaign.Locations)
            LocationList.Items.Add(l.ListDisplay);
        _selectedLocationIdx = -1;
        ClearLocationEditor();
    }

    private void LocationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = LocationList.SelectedIndex;
        if (idx < 0 || idx >= _app.Campaign.Locations.Count) return;
        _selectedLocationIdx = idx;
        var l = _app.Campaign.Locations[idx];
        LocName.Text  = l.Name;
        LocType.Text  = l.Type;
        LocNotes.Text = l.Notes;
    }

    private void BtnNewLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateCampaignEntryInCurrentLicense())
            return;

        _app.Campaign.Locations.Add(new LocationEntry { Name = "New Location" });
        RefreshLocationList();
        LocationList.SelectedIndex = _app.Campaign.Locations.Count - 1;
    }

    private void BtnDeleteLocation_Click(object sender, RoutedEventArgs e)
    {
        int idx = LocationList.SelectedIndex;
        if (idx < 0) return;
        var result = MessageBox.Show("Delete this location?", "Confirm",
                                     MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _app.Campaign.Locations.RemoveAt(idx);
        RefreshLocationList();
    }

    private void BtnSaveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLocationIdx < 0 || _selectedLocationIdx >= _app.Campaign.Locations.Count) return;
        var l = _app.Campaign.Locations[_selectedLocationIdx];
        l.Name  = LocName.Text.Trim();
        l.Type  = LocType.Text.Trim();
        l.Notes = LocNotes.Text.Trim();
        RefreshLocationList();
        LocationList.SelectedIndex = _selectedLocationIdx;
    }

    private void ClearLocationEditor()
    {
        LocName.Text = LocType.Text = LocNotes.Text = "";
    }

    private bool CanCreateCampaignEntryInCurrentLicense()
    {
        if (!_app.License.IsDemoMode)
            return true;

        int totalEntries = _app.Campaign.Entries.Count + _app.Campaign.Npcs.Count + _app.Campaign.Locations.Count;
        if (totalEntries < _app.License.DemoMaxCampaignEntries)
            return true;

        MessageBox.Show(
            $"Demo mode allows up to {_app.License.DemoMaxCampaignEntries} campaign entries in total (log + NPCs + locations).",
            "Demo Limit",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }
}
