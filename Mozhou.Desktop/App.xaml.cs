using System.Windows;

namespace Mozhou.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) => { MessageBox.Show("操作未完成：" + a.Exception.Message + "\n当前内容仍留在窗口中，请尝试另存或备份。", "墨舟", MessageBoxButton.OK, MessageBoxImage.Warning); a.Handled = true; };
        new MainWindow(e.Args.FirstOrDefault()).Show();
    }
}
