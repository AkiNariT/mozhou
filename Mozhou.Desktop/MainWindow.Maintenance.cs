using System.Windows;

namespace Mozhou.Desktop;

public partial class MainWindow
{
    private MaintenanceWindow? _maintenance;

    private void MaintenanceClick(object sender, RoutedEventArgs e) => ShowMaintenance();

    private void ShowMaintenance()
    {
        if (_maintenance != null) { _maintenance.Activate(); return; }
        _maintenance = new MaintenanceWindow { Owner = this, Icon = Icon };
        _maintenance.Closed += (_, _) => _maintenance = null;
        _maintenance.Show();
    }
}
