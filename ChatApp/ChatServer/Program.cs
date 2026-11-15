using ChatServer;

int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 5000;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var hub = new ChatHub(port);
hub.Log += msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");

Console.WriteLine("Press Ctrl+C to stop.");
await hub.RunAsync(cts.Token);
Console.WriteLine("Server stopped.");
