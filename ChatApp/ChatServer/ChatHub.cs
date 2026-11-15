using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace ChatServer;

/// <summary>
/// Multi-client chat server (syllabus Ch.2 + Ch.5).
/// Protocol (UTF-8, one message per line, '|' separates fields):
///   client -> server : JOIN|name   MSG|text   QUIT
///   server -> client : SYS|text    MSG|name|text   USERS|a,b,c   ERR|text
/// </summary>
public sealed class ChatHub
{
    private const int MaxNameLength = 20;
    private const int MaxMessageLength = 1000;

    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, ClientSession> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Log;

    public ChatHub(int port) => _listener = new TcpListener(IPAddress.Any, port);

    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();
        Log?.Invoke($"Server listening on port {((IPEndPoint)_listener.LocalEndpoint).Port}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp = await _listener.AcceptTcpClientAsync(ct);
                // Fire-and-forget: each client is served concurrently by its own task.
                _ = HandleClientAsync(tcp, ct);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally
        {
            _listener.Stop();
            foreach (var c in _clients.Values) c.Dispose();
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        var session = new ClientSession(tcp);
        Log?.Invoke($"Connection from {session.Endpoint}");
        bool joined = false;

        try
        {
            // 1) The first line must be JOIN|name
            string? first = await session.Reader.ReadLineAsync(ct);
            string? name = ParseJoin(first);
            if (name == null)
            {
                await session.SendAsync($"ERR|Invalid name (1-{MaxNameLength} chars, no '|' or ',').");
                return;
            }
            if (!_clients.TryAdd(name, session))
            {
                await session.SendAsync("ERR|That name is already in use.");
                return;
            }

            session.Name = name;
            joined = true;
            Log?.Invoke($"{name} joined ({session.Endpoint})");
            await session.SendAsync($"SYS|Welcome, {name}!");
            await BroadcastAsync($"SYS|{name} joined the chat");
            await BroadcastUsersAsync();

            // 2) Main loop: read messages until QUIT / disconnect
            while (!ct.IsCancellationRequested)
            {
                string? line = await session.Reader.ReadLineAsync(ct);
                if (line == null || line == "QUIT") break; // client closed

                if (line.StartsWith("MSG|", StringComparison.Ordinal))
                {
                    string text = line[4..].Trim();
                    if (text.Length == 0) continue;
                    if (text.Length > MaxMessageLength) text = text[..MaxMessageLength];
                    await BroadcastAsync($"MSG|{name}|{text}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* client dropped the connection */ }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            if (joined)
            {
                _clients.TryRemove(new KeyValuePair<string, ClientSession>(session.Name, session));
                Log?.Invoke($"{session.Name} left");
                await BroadcastAsync($"SYS|{session.Name} left the chat");
                await BroadcastUsersAsync();
            }
            session.Dispose();
        }
    }

    private static string? ParseJoin(string? line)
    {
        if (line == null || !line.StartsWith("JOIN|", StringComparison.Ordinal)) return null;
        string name = line[5..].Trim();
        if (name.Length is 0 or > MaxNameLength) return null;
        if (name.Contains('|') || name.Contains(',')) return null;
        return name;
    }

    private Task BroadcastUsersAsync() =>
        BroadcastAsync("USERS|" + string.Join(",", _clients.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));

    private async Task BroadcastAsync(string line)
    {
        var sends = _clients.Values.Select(async c =>
        {
            try { await c.SendAsync(line); }
            catch { /* that client is gone; its own loop will clean it up */ }
        });
        await Task.WhenAll(sends);
    }
}
