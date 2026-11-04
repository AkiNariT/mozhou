using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Mozhou.Desktop;

internal static class MaintenanceWindowChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        var window = new MaintenanceWindow(client, installed: true);
        var button = (Button)window.FindName("CheckButton");
        var download = (Button)window.FindName("DownloadButton");
        var status = (TextBlock)window.FindName("UpdateStatus");
        void Click() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        check(handler.Endpoint == null && ((UIElement)window.FindName("UninstallPanel")).Visibility == Visibility.Visible,
            "installed maintenance shows uninstall and waits for a manual check");
        Click();
        check(handler.Endpoint?.AbsoluteUri == "https://api.github.com/repos/AkiNariT/mozhou/releases/latest"
            && download.Visibility == Visibility.Visible, "a manual check queries the official release API and offers its new release");
        handler.Failure = new HttpRequestException("offline");
        Click();
        check(button.IsEnabled && download.Visibility == Visibility.Collapsed && status.Text.Contains("网络"),
            "going offline clears the stale update action and allows retry");
        handler.Failure = new TaskCanceledException("timeout");
        Click();
        check(button.IsEnabled && status.Text.Contains("超时"), "timeout explains the failed check and allows retry");
        handler.Failure = null;
        var current = typeof(MaintenanceWindow).Assembly.GetName().Version!.ToString(3);
        handler.Tag = "v" + current;
        Click();
        check(button.IsEnabled && status.Text.Contains("已是最新") && download.Visibility == Visibility.Collapsed,
            "retry after failure recognizes the current formal version");
        window.Close();

        var portable = new MaintenanceWindow(client, installed: false);
        check(((UIElement)portable.FindName("UninstallPanel")).Visibility == Visibility.Collapsed,
            "portable maintenance does not offer the installed-app uninstall action");
        portable.Close();
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        internal string Tag = "v99.0.0";
        internal Uri? Endpoint;
        internal Exception? Failure;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Endpoint = request.RequestUri;
            return Failure != null ? Task.FromException<HttpResponseMessage>(Failure)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"tag_name":"{{Tag}}","draft":false,"prerelease":false,"assets":[]}""")
                });
        }
    }
}
