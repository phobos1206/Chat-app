using System.Net.Sockets;
using System.Text;

namespace ChatClient;

/// <summary>Network layer of the client (no UI code here). Syllabus Ch.2 + Ch.4.</summary>
public sealed class ChatConnection : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    /// <summary>Raised for every complete line received from the server.</summary>
    public event Action<string>? LineReceived;

    public async Task ConnectAsync(string host, int port, string name)
    {
        _tcp = new TcpClient();
        // Timeout so the UI never waits forever for an unreachable server.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _tcp.ConnectAsync(host, port, timeout.Token);

        NetworkStream stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Utf8);
        _writer = new StreamWriter(stream, Utf8) { AutoFlush = true, NewLine = "\n" };
        await SendAsync("JOIN|" + name);
    }

    /// <summary>Reads lines until the server closes the connection or ct is cancelled.</summary>
    public async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_reader == null) throw new InvalidOperationException("Not connected.");
        while (true)
        {
            string? line = await _reader.ReadLineAsync(ct);
            if (line == null) break; // server closed
            LineReceived?.Invoke(line);
        }
    }

    public async Task SendAsync(string line)
    {
        if (_writer == null) throw new InvalidOperationException("Not connected.");
        await _sendLock.WaitAsync();
        try { await _writer.WriteLineAsync(line); }
        finally { _sendLock.Release(); }
    }

    public void Dispose()
    {
        try { _tcp?.Close(); } catch { }
        _sendLock.Dispose();
    }
}
