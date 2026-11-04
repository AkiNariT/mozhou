using System.Windows;

namespace Mozhou.Desktop;
public partial class App : Application
{
    private Mutex? _installedInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) => { MessageBox.Show("操作未完成：" + a.Exception.Message + "\n当前内容仍留在窗口中，请尝试另存或备份。", "墨舟", MessageBoxButton.OK, MessageBoxImage.Warning); a.Handled = true; };
        try
        {
            if (SettingsStore.IsInstalled) _installedInstance = new Mutex(false, @"Local\Mozhou.Desktop.Running");
            SettingsStore.Initialize(ex => MessageBox.Show(
                "旧版资料暂时无法导入。\n" + ex.Message +
                "\n\n重新连接旧盘或关闭占用文件的程序后，选择“是”重试。\n选择“否”跳过自动导入，继续使用当前书架和设置。以后仍可通过“打开 / 导入小说”添加旧稿。",
                "墨舟 · 重试导入旧版资料？", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes) == MessageBoxResult.Yes);
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法准备墨舟用户资料。请检查用户资料目录的访问权限和磁盘空间后重新启动。\n" + ex.Message, "墨舟", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }
        new MainWindow(e.Args.FirstOrDefault()).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _installedInstance?.Dispose();
        base.OnExit(e);
    }
}
