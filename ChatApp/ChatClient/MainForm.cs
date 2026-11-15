namespace ChatClient;

/// <summary>
/// WinForms chat window. Every await below resumes on the UI thread, so
/// controls can be updated directly (syllabus Ch.4: "Updating the UI safely").
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _host = new() { Text = "127.0.0.1", Width = 110 };
    private readonly TextBox _port = new() { Text = "5000", Width = 50 };
    private readonly TextBox _name = new() { Width = 100, PlaceholderText = "Your name" };
    private readonly Button _btnConnect = new() { Text = "Connect", AutoSize = true };
    private readonly RichTextBox _chat = new() { ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly ListBox _users = new() { Dock = DockStyle.Right, Width = 150 };
    private readonly TextBox _message = new() { Dock = DockStyle.Fill, Enabled = false };
    private readonly Button _btnSend = new() { Text = "Send", Dock = DockStyle.Right, Width = 80, Enabled = false };

    private ChatConnection? _conn;
    private CancellationTokenSource? _cts;
    private string _myName = "";

    public MainForm()
    {
        Text = "Chat App";
        Width = 760;
        Height = 520;
        MinimumSize = new Size(560, 360);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Host:", AutoSize = true, Margin = new Padding(3, 7, 0, 0) }, _host,
            new Label { Text = "Port:", AutoSize = true, Margin = new Padding(3, 7, 0, 0) }, _port,
            new Label { Text = "Name:", AutoSize = true, Margin = new Padding(3, 7, 0, 0) }, _name,
            _btnConnect
        });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(6, 4, 6, 4) };
        bottom.Controls.Add(_message);   // Fill first,
        bottom.Controls.Add(_btnSend);   // then edge-docked control

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(_chat);      // Fill first,
        center.Controls.Add(_users);     // then edge-docked control

        Controls.Add(center);
        Controls.Add(bottom);
        Controls.Add(top);

        _btnConnect.Click += OnConnectClick;
        _btnSend.Click += async (_, _) => await SendCurrentAsync();
        _message.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendCurrentAsync();
            }
        };
        FormClosing += async (_, _) => await DisconnectAsync();
    }

    // ---------- connect / disconnect ----------

    private async void OnConnectClick(object? sender, EventArgs e)
    {
        if (_conn != null) { await DisconnectAsync(); return; }

        string name = _name.Text.Trim();
        if (name.Length == 0) { AppendLine("Please enter a name.", Color.Firebrick); return; }
        if (!int.TryParse(_port.Text, out int port) || port is < 1 or > 65535)
        {
            AppendLine("Invalid port.", Color.Firebrick);
            return;
        }

        _btnConnect.Enabled = false;
        var conn = new ChatConnection();
        conn.LineReceived += HandleLine;
        try
        {
            AppendLine($"Connecting to {_host.Text}:{port} ...", Color.Gray);
            await conn.ConnectAsync(_host.Text.Trim(), port, name);

            _conn = conn;
            _myName = name;
            _cts = new CancellationTokenSource();
            SetConnected(true);
            _ = RunReceiveLoopAsync(conn, _cts.Token);
        }
        catch (Exception ex)
        {
            AppendLine($"Cannot connect: {ex.Message}", Color.Firebrick);
            conn.Dispose();
        }
        finally
        {
            _btnConnect.Enabled = true;
        }
    }

    private async Task RunReceiveLoopAsync(ChatConnection conn, CancellationToken ct)
    {
        try { await conn.ReceiveLoopAsync(ct); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException ex) { AppendLine($"Connection error: {ex.Message}", Color.Firebrick); }
        finally
        {
            // If the user did not click Disconnect, the server closed on us.
            if (_conn == conn)
            {
                await DisconnectAsync();
                AppendLine("Disconnected from server.", Color.Gray);
            }
        }
    }

    private async Task DisconnectAsync()
    {
        ChatConnection? conn = _conn;
        if (conn == null) return;
        _conn = null;

        _cts?.Cancel();
        try { await conn.SendAsync("QUIT"); } catch { }
        conn.Dispose();
        _cts?.Dispose();
        _cts = null;

        if (!IsDisposed) SetConnected(false);
    }

    private void SetConnected(bool connected)
    {
        _btnConnect.Text = connected ? "Disconnect" : "Connect";
        _host.Enabled = _port.Enabled = _name.Enabled = !connected;
        _message.Enabled = _btnSend.Enabled = connected;
        if (connected) _message.Focus(); else _users.Items.Clear();
    }

    // ---------- sending ----------

    private async Task SendCurrentAsync()
    {
        string text = _message.Text.Trim();
        if (text.Length == 0 || _conn == null) return;
        _message.Clear();
        try { await _conn.SendAsync("MSG|" + text); }
        catch (Exception ex) { AppendLine($"Send failed: {ex.Message}", Color.Firebrick); }
    }

    // ---------- receiving ----------

    private void HandleLine(string line)
    {
        int i = line.IndexOf('|');
        string kind = i < 0 ? line : line[..i];
        string rest = i < 0 ? "" : line[(i + 1)..];

        switch (kind)
        {
            case "MSG":
                int j = rest.IndexOf('|');
                if (j > 0)
                {
                    string from = rest[..j];
                    string text = rest[(j + 1)..];
                    bool mine = string.Equals(from, _myName, StringComparison.OrdinalIgnoreCase);
                    AppendLine($"[{DateTime.Now:HH:mm}] {from}: {text}", mine ? Color.DarkGreen : Color.Black);
                }
                break;
            case "SYS":
                AppendLine("* " + rest, Color.Gray);
                break;
            case "ERR":
                AppendLine("Error: " + rest, Color.Firebrick);
                break;
            case "USERS":
                _users.BeginUpdate();
                _users.Items.Clear();
                foreach (string u in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    _users.Items.Add(u);
                _users.EndUpdate();
                break;
        }
    }

    private void AppendLine(string text, Color color)
    {
        _chat.SelectionStart = _chat.TextLength;
        _chat.SelectionLength = 0;
        _chat.SelectionColor = color;
        _chat.AppendText(text + Environment.NewLine);
        _chat.ScrollToCaret();
    }
}
