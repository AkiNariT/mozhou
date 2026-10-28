using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Mozhou.Desktop.Tests")]

namespace Mozhou.Desktop;

/// <summary>Accepts background deltas; the owner flushes and completes on its presentation thread.</summary>
internal sealed class BufferedTextStream(Action<string> onBatch) : IDisposable
{
    private readonly object gate = new();
    private readonly StringBuilder received = new(), pending = new();
    private Action<string>? present = onBatch;
    private bool stopped;
    private string completedText = "";

    public void Append(string delta)
    {
        if (delta.Length == 0) return;
        lock (gate)
        {
            if (stopped) return;
            received.Append(delta);
            pending.Append(delta);
        }
    }

    public void Flush()
    {
        lock (gate)
        {
            if (!stopped) FlushPending();
        }
    }

    public string Complete()
    {
        lock (gate)
        {
            if (stopped) return completedText;
            stopped = true;
            completedText = received.ToString();
            received.Clear();
            try { FlushPending(); }
            finally { present = null; }
            return completedText;
        }
    }

    private void FlushPending()
    {
        if (pending.Length == 0) return;
        var batch = pending.ToString();
        pending.Clear();
        // Keep cleanup and presentation serialized so a queued flush cannot outlive Dispose.
        present?.Invoke(batch);
    }

    public void Dispose()
    {
        lock (gate)
        {
            stopped = true;
            present = null;
            received.Clear(); pending.Clear(); completedText = "";
        }
    }
}
