using System.Text;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Mozhou.Codex;
using Mozhou.Desktop;

internal static class StreamingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Reverting to per-delta presentation must fail the batching assertion.
        var displayed = new StringBuilder();
        var flushes = 0;
        using (var stream = new BufferedTextStream(batch => { displayed.Append(batch); flushes++; }))
        {
            for (var i = 0; i < 5000; i++)
            {
                stream.Append("墨"); stream.Append("舟"); stream.Append("。");
                if (i % 137 == 0) stream.Flush();
            }
            var result = stream.Complete();
            var expected = string.Concat(Enumerable.Repeat("墨舟。", 5000));
            check(result == expected && displayed.ToString() == expected, "batched short deltas preserve the complete answer in order");
            check(flushes < 50, "15000 short deltas require fewer than 50 presentation updates");
            var finishedFlushes = flushes;
            stream.Append("late delta"); stream.Flush(); stream.Complete();
            check(flushes == finishedFlushes && displayed.ToString() == expected, "completed stream ignores late deltas and scheduled flushes");
        }

        // Cancellation and errors can arrive before the next timer tick.
        foreach (var ending in new[] { "cancel", "error" })
        {
            displayed.Clear();
            using var stream = new BufferedTextStream(batch => displayed.Append(batch));
            stream.Append("已经显示"); stream.Flush();
            stream.Append("，仍在缓冲的尾段。");
            var partial = stream.Complete();
            check(partial == "已经显示，仍在缓冲的尾段。" && displayed.ToString() == partial, ending + " preserves the pending tail before final status text");
        }

        var callbacks = 0;
        var disposed = new BufferedTextStream(_ => callbacks++);
        disposed.Append("pending at cleanup");
        disposed.Dispose();
        disposed.Append("late"); disposed.Flush(); disposed.Complete(); disposed.Dispose();
        check(callbacks == 0, "disposed stream cannot invoke a presentation callback");

        displayed.Clear();
        using (var stream = new BufferedTextStream(batch => displayed.Append(batch)))
        {
            Parallel.For(0, 8000, _ => stream.Append("甲"));
            stream.Flush();
            stream.Append("尾");
            var result = stream.Complete();
            check(result == new string('甲', 8000) + "尾" && displayed.ToString() == result, "concurrent producers cannot lose or corrupt buffered deltas");
        }

        using (var stream = new BufferedTextStream(_ => throw new InvalidOperationException("unexpected empty callback")))
        {
            stream.Append(""); stream.Flush();
            check(stream.Complete() == "", "empty stream completes without a presentation callback");
        }

        // This client is deliberately never started: cleanup must not need an account or model.
        var window = new MainWindow(null);
        var client = new CodexClient("never-started-test-fixture.exe", AppContext.BaseDirectory);
        try
        {
            Field("_codex").SetValue(window, client);
            var models = (ComboBox)window.FindName("ModelPicker");
            var send = (Button)window.FindName("SendButton");
            models.ItemsSource = new[] { new CodexModel("old-model", "Old model", true) };
            models.IsEnabled = true; send.IsEnabled = true;
            var cleanup = typeof(MainWindow).GetMethod("DisconnectCodexAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((Task)cleanup.Invoke(window, null)!).GetAwaiter().GetResult();
            check(Field("_codex").GetValue(window) == null && models.ItemsSource == null && !models.IsEnabled && !send.IsEnabled, "failed-connection cleanup clears the client and stale model controls");
            var rejected = false;
            try { client.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (ObjectDisposedException) { rejected = true; }
            check(rejected, "failed-connection cleanup disposes the owned client before another connection");
        }
        finally
        {
            ((DispatcherTimer)Field("_saveTimer").GetValue(window)!).Stop();
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
}
