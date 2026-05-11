using System.Windows;

namespace DungeonMasterCortex.Views;

/// <summary>Contract every screen must implement.</summary>
public interface IScreen
{
    UIElement View    { get; }
    void      OnEnter();
}
