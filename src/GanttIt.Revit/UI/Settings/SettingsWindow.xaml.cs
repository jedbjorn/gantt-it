using System.Windows;
using GanttIt.Revit.UI.Theming;

namespace GanttIt.Revit.UI.Settings;

/// <summary>The Settings dialog (§2.3). All behaviour is in <see cref="SettingsViewModel"/>.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        DataContext = model;
        model.CloseRequested += () =>
        {
            if (IsVisible) Close();
        };
    }
}
