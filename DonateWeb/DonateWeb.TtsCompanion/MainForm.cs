using System.Globalization;
using System.Net.Http.Json;
using System.Speech.Synthesis;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;

namespace DonateWeb.TtsCompanion;

internal sealed class MainForm : Form
{
    private readonly TextBox _server = new() { PlaceholderText = "https://example.com" };
    private readonly TextBox _slug = new() { PlaceholderText = "slug-streamer" };
    private readonly TextBox _token = new() { UseSystemPasswordChar = true, PlaceholderText = "Widget Token" };
    private readonly Label _status = new() { AutoSize = true, Text = "Chưa kết nối" };
    private readonly Button _connect = new() { Text = "Kết nối" };
    private readonly NotifyIcon _tray;
    private readonly SpeechSynthesizer _speech = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly object _seenLock = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 3500 };
    private readonly System.Windows.Forms.Timer _retryTimer = new() { Interval = 5000 };
    private HubConnection? _hub;
    private int _cursor;
    private bool _cursorInitialized;
    private bool _started;
    private bool _voiceAvailable;

    public MainForm()
    {
        Text = "DonateWeb - Trợ lý đọc donate";
        MinimumSize = new Size(520, 300);
        StartPosition = FormStartPosition.CenterScreen;
        var settings = StoredSettings.Load();
        _server.Text = settings.ServerUrl;
        _slug.Text = settings.StreamerSlug;
        _token.Text = settings.WidgetToken;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Địa chỉ VPS", _server);
        AddRow(layout, 1, "Slug streamer", _slug);
        AddRow(layout, 2, "Widget Token", _token);
        layout.Controls.Add(new Label { Text = "Trạng thái", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_status, 1, 3);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var save = new Button { Text = "Lưu cấu hình" };
        var test = new Button { Text = "Thử giọng" };
        save.Click += (_, _) => SaveSettings();
        test.Click += (_, _) => Speak("Đây là giọng đọc thử. Khán giả vừa ủng hộ năm mươi nghìn đồng.");
        _connect.Click += async (_, _) =>
        {
            if (_started && _hub is null)
            {
                _started = false;
                _retryTimer.Stop();
                _pollTimer.Stop();
                _cursorInitialized = false;
                _connect.Text = "Kết nối";
                SetStatus("Đã hủy kết nối lại");
                return;
            }
            await ToggleConnectionAsync();
        };
        buttons.Controls.AddRange([save, test, _connect]);
        layout.Controls.Add(buttons, 1, 4);
        Controls.Add(layout);

        _tray = new NotifyIcon
        {
            Text = "DonateWeb TTS",
            Icon = SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Mở cấu hình", null, (_, _) => ShowFromTray());
        _tray.ContextMenuStrip.Items.Add("Thoát", null, (_, _) => ExitApp());
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _pollTimer.Tick += async (_, _) => await PollCatchupAsync();
        _retryTimer.Tick += async (_, _) =>
        {
            if (_hub is null && _started) await ToggleConnectionAsync();
        };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) => { if (!_started) { _tray.Visible = false; _speech.Dispose(); _http.Dispose(); } else { e.Cancel = true; Hide(); } };
        try
        {
            _speech.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, CultureInfo.GetCultureInfo("vi-VN"));
            _voiceAvailable = true;
        }
        catch (Exception ex) { SetStatus("Chưa tìm thấy giọng vi-VN: " + ex.Message); }
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4, 6, 4, 6);
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private async Task ToggleConnectionAsync()
    {
        if (_hub is not null) { await DisconnectAsync(); return; }
        var settings = SaveSettings();
        if (settings is null) return;
        try
        {
            if (!_cursorInitialized)
            {
                _cursor = await GetBootstrapCursorAsync(settings);
                _cursorInitialized = true;
            }
            _hub = new HubConnectionBuilder().WithUrl(new Uri(new Uri(settings.ServerUrl), "/paymentHub"))
                .WithAutomaticReconnect().Build();
            _hub.On<DonationAlert>("ReceiveTtsAlert", alert => Receive(alert));
            _hub.On<DonationAlert>("ReceiveTtsTest", alert => Receive(alert));
            _hub.Reconnecting += _ => { SetStatus("Đang kết nối lại..."); return Task.CompletedTask; };
            _hub.Reconnected += async _ => { await JoinGroupAsync(settings); SetStatus("Đã kết nối"); };
            _hub.Closed += async _ =>
            {
                var closed = _hub;
                _hub = null;
                if (closed is not null) await closed.DisposeAsync();
                if (_started)
                {
                    _retryTimer.Start();
                    _connect.Text = "Đang thử kết nối lại";
                    SetStatus("Mất kết nối, sẽ tự kết nối lại.");
                }
            };
            await _hub.StartAsync();
            await JoinGroupAsync(settings);
            _pollTimer.Start();
            _started = true;
            _retryTimer.Stop();
            _connect.Text = "Ngắt kết nối";
            SetStatus("Đã kết nối");
        }
        catch (Exception ex)
        {
            _started = true;
            _retryTimer.Start();
            if (_cursorInitialized) _pollTimer.Start();
            _connect.Text = "Đang thử kết nối lại";
            SetStatus("Lỗi kết nối: " + ex.Message);
            if (_hub is not null) { await _hub.DisposeAsync(); _hub = null; }
        }
    }

    private async Task JoinGroupAsync(StoredSettings settings) =>
        await _hub!.InvokeAsync("JoinTtsGroup", settings.StreamerSlug, settings.WidgetToken);

    private async Task<int> GetBootstrapCursorAsync(StoredSettings settings)
    {
        using var request = CreateRequest(settings, null);
        var result = await _http.SendAsync(request);
        result.EnsureSuccessStatusCode();
        var data = await result.Content.ReadFromJsonAsync<CatchupResponse>();
        return data?.Cursor ?? 0;
    }

    private async Task PollCatchupAsync()
    {
        if (!_started || !_cursorInitialized) return;
        try
        {
            var settings = CurrentSettings();
            using var response = await _http.SendAsync(CreateRequest(settings, _cursor));
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CatchupResponse>();
            if (result is null) return;
            foreach (var alert in result.Alerts) Receive(alert);
            _cursor = Math.Max(_cursor, result.Cursor);
        }
        catch (Exception ex) { SetStatus("Đang chờ VPS: " + ex.Message); }
    }

    private HttpRequestMessage CreateRequest(StoredSettings settings, int? afterId)
    {
        var url = new Uri(new Uri(settings.ServerUrl), $"api/widgets/tts/alerts/{Uri.EscapeDataString(settings.StreamerSlug)}" +
            (afterId.HasValue ? $"?afterId={afterId.Value}" : ""));
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Widget-Token", settings.WidgetToken);
        return request;
    }

    private void Receive(DonationAlert alert)
    {
        var id = alert.DonationId > 0 ? "donation:" + alert.DonationId : "test:" + alert.EventId;
        lock (_seenLock)
        {
            if (!_seen.Add(id)) return;
            _seenOrder.Enqueue(id);
            while (_seenOrder.Count > 500) _seen.Remove(_seenOrder.Dequeue());
        }
        var text = $"{Clean(alert.DonorName, 100)} vừa ủng hộ {alert.Amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} đồng.";
        var message = Clean(alert.Message, 220);
        if (message.Length > 0) text += " Lời nhắn: " + message;
        Speak(text);
    }

    private void Speak(string text)
    {
        if (!_voiceAvailable) { SetStatus("Không có giọng vi-VN trên Windows; hãy cài Microsoft An."); return; }
        try { _speech.SpeakAsync(text); }
        catch (Exception ex) { SetStatus("Không phát được giọng: " + ex.Message); }
    }

    private static string Clean(string? value, int length) => new string((value ?? "").Where(c => !char.IsControl(c)).Take(length).ToArray()).Trim();

    private StoredSettings? SaveSettings()
    {
        var baseUriText = _server.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUriText, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(_slug.Text) || string.IsNullOrWhiteSpace(_token.Text))
        {
            SetStatus("Nhập URL HTTPS, slug và Widget Token hợp lệ.");
            return null;
        }
        var settings = new StoredSettings(baseUriText, _slug.Text.Trim().ToLowerInvariant(), _token.Text.Trim());
        try { settings.Save(); SetStatus("Đã lưu cấu hình trên máy này."); }
        catch (Exception ex) { SetStatus("Không lưu được cấu hình: " + ex.Message); }
        return settings;
    }

    private StoredSettings CurrentSettings() => new(_server.Text.Trim().TrimEnd('/'), _slug.Text.Trim().ToLowerInvariant(), _token.Text.Trim());
    private void SetStatus(string status) { if (IsHandleCreated) BeginInvoke(() => _status.Text = status); }

    private async Task DisconnectAsync()
    {
        _started = false;
        _pollTimer.Stop();
        _retryTimer.Stop();
        _cursorInitialized = false;
        if (_hub is not null) { await _hub.DisposeAsync(); _hub = null; }
        _connect.Text = "Kết nối";
        SetStatus("Đã ngắt kết nối");
    }

    private void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private async void ExitApp() { await DisconnectAsync(); _tray.Visible = false; _speech.Dispose(); _http.Dispose(); _started = false; Close(); }

    private sealed class CatchupResponse
    {
        [JsonPropertyName("cursor")] public int Cursor { get; set; }
        [JsonPropertyName("alerts")] public List<DonationAlert> Alerts { get; set; } = [];
    }

    private sealed class DonationAlert
    {
        [JsonPropertyName("eventId")] public Guid EventId { get; set; }
        [JsonPropertyName("donationId")] public int DonationId { get; set; }
        [JsonPropertyName("donorName")] public string DonorName { get; set; } = "";
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }
}
