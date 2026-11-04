using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace Mozhou.Desktop;

public partial class MaintenanceWindow : Window
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1024 * 1024
    };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Version _current;
    private readonly bool _installed;
    private readonly HttpClient _client;
    private string? _updateUrl;

    public MaintenanceWindow() : this(SharedClient, SettingsStore.IsInstalled) { }

    internal MaintenanceWindow(HttpClient client, bool installed)
    {
        _client = client;
        _installed = installed;
        InitializeComponent();
        var assemblyVersion = typeof(MaintenanceWindow).Assembly.GetName().Version!;
        _current = new Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        CurrentLabel.Text = $"墨舟 {_current}  ·  {(_installed ? "安装版" : "便携版")}";
        UninstallPanel.Visibility = _installed ? Visibility.Visible : Visibility.Collapsed;
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private async void CheckClick(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        DownloadButton.Visibility = Visibility.Collapsed;
        _updateUrl = null;
        UpdateStatus.Text = "正在检查 GitHub 上的最新正式版…";
        var token = _lifetime.Token;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseUpdate.LatestApi);
            request.Headers.UserAgent.ParseAdd($"Mozhou/{_current}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _client.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(token);
            var release = ReleaseUpdate.Read(json, _current, _installed);
            if (token.IsCancellationRequested) return;
            if (release.ActionUrl == null)
            {
                UpdateStatus.Text = release.Latest == _current ? $"当前已是最新正式版（{_current}）。"
                    : $"当前版本 {_current} 高于公开正式版 {release.Latest}，无需更新。";
                return;
            }
            _updateUrl = release.ActionUrl;
            var instruction = _installed ? "保存并关闭墨舟后运行新版安装包，即可保留资料并完成升级。"
                : "请前往发布页查看适用于便携版的下载和说明。";
            UpdateStatus.Text = $"发现新版本 {release.Latest}。{instruction}";
            DownloadButton.Content = release.IsInstaller ? "下载安装包" : "打开版本发布页";
            DownloadButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException) { UpdateStatus.Text = "检查更新超时，请稍后重试。"; }
        catch (HttpRequestException ex)
        {
            UpdateStatus.Text = ex.StatusCode switch
            {
                null => "无法连接 GitHub。请检查网络连接后重试。",
                HttpStatusCode.NotFound => "暂时找不到公开的正式版本，请稍后重试。",
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => "GitHub 暂时限制了更新查询，请稍后重试。",
                _ => $"更新服务暂不可用（HTTP {(int)ex.StatusCode.Value}），请稍后重试。"
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            UpdateStatus.Text = "无法识别发布信息，请稍后重试。";
        }
        finally { if (!token.IsCancellationRequested) CheckButton.IsEnabled = true; }
    }

    private void DownloadClick(object sender, RoutedEventArgs e)
    {
        if (_updateUrl != null) OpenTarget(_updateUrl, "无法打开浏览器，请确认 Windows 中已设置默认浏览器。");
    }

    private void UninstallClick(object sender, RoutedEventArgs e)
    {
        if (_installed) OpenTarget("ms-settings:appsfeatures", "请手动打开「Windows 设置 → 应用」，找到「墨舟」并选择卸载。");
    }

    private void OpenTarget(string target, string failureMessage)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, failureMessage, "墨舟", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
