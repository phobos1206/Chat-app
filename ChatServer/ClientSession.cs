using System.Net.Sockets;
using System.Text;

namespace ChatServer;

/// <summary>One connected client: its socket, text reader/writer and display name.</summary>
internal sealed class ClientSession : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public TcpClient Tcp { get; }
    public StreamReader Reader { get; }
    public StreamWriter Writer { get; }
    public string Name { get; set; } = "";
    public string Endpoint { get; }

    public ClientSession(TcpClient tcp)
    {
        Tcp = tcp;
        Endpoint = tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
        NetworkStream stream = tcp.GetStream();
        // StreamReader accumulates bytes until '\n', so TCP's byte-stream
        // behaviour (split / merged packets) is handled for us.
        Reader = new StreamReader(stream, Utf8);
        Writer = new StreamWriter(stream, Utf8) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>Only one task may write at a time, otherwise two broadcasts could interleave.</summary>
    public async Task SendAsync(string line)
    {
        await _sendLock.WaitAsync();
        try { await Writer.WriteLineAsync(line); }
        finally { _sendLock.Release(); }
    }

    public void Dispose()
    {
        try { Tcp.Close(); } catch { /* already closed */ }
        _sendLock.Dispose();
    }
}
