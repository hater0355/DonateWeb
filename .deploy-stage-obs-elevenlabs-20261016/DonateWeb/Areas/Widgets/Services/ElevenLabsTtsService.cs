using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DonateWeb.Areas.Widgets.Services;

/// <summary>Cấu hình ElevenLabs được đọc từ biến môi trường trên máy chủ.</summary>
public sealed class ElevenLabsOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string VoiceId { get; set; } = string.Empty;
    public string ModelId { get; set; } = "eleven_flash_v2_5";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(VoiceId);
}

public interface IElevenLabsTtsService
{
    bool IsConfigured { get; }
    Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Gọi ElevenLabs từ máy chủ để API key không bao giờ xuất hiện trong OBS hoặc trình duyệt.
/// </summary>
public sealed class ElevenLabsTtsService : IElevenLabsTtsService
{
    private readonly HttpClient _httpClient;
    private readonly ElevenLabsOptions _options;
    private readonly ILogger<ElevenLabsTtsService> _logger;

    public ElevenLabsTtsService(
        HttpClient httpClient,
        IOptions<ElevenLabsOptions> options,
        ILogger<ElevenLabsTtsService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ElevenLabs chưa được cấu hình trên máy chủ.");

        var voiceId = Uri.EscapeDataString(_options.VoiceId.Trim());
        var endpoint = new Uri(
            $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format=mp3_44100_128");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("xi-api-key", _options.ApiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                text,
                model_id = string.IsNullOrWhiteSpace(_options.ModelId)
                    ? "eleven_flash_v2_5"
                    : _options.ModelId.Trim(),
                // Buộc ElevenLabs nhận diện nội dung là tiếng Việt.
                language_code = "vi"
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ElevenLabs trả về HTTP {StatusCode} khi tạo giọng đọc.", (int)response.StatusCode);
            throw new HttpRequestException(
                $"ElevenLabs trả về HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

/// <summary>
/// Bộ nhớ đệm giới hạn để OBS tải lại hoặc phát trùng alert không tạo thêm lượt tính phí.
/// </summary>
public interface IDonationTtsAudioCache
{
    bool TryGet(string key, out byte[] audio);
    void Set(string key, byte[] audio);
}

public sealed class DonationTtsAudioCache : IDonationTtsAudioCache
{
    private const int MaxEntries = 100;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
    private readonly object _gate = new();
    private readonly Dictionary<string, (byte[] Audio, DateTimeOffset ExpiresAt)> _entries = new();
    private readonly Queue<string> _insertionOrder = new();

    public bool TryGet(string key, out byte[] audio)
    {
        lock (_gate)
        {
            RemoveExpiredEntries(DateTimeOffset.UtcNow);
            if (_entries.TryGetValue(key, out var entry))
            {
                audio = entry.Audio;
                return true;
            }
        }

        audio = Array.Empty<byte>();
        return false;
    }

    public void Set(string key, byte[] audio)
    {
        if (audio.Length == 0)
            return;

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            RemoveExpiredEntries(now);
            if (_entries.ContainsKey(key))
                return;

            _entries[key] = (audio, now.Add(CacheLifetime));
            _insertionOrder.Enqueue(key);
            while (_entries.Count > MaxEntries && _insertionOrder.TryDequeue(out var oldest))
                _entries.Remove(oldest);
        }
    }

    private void RemoveExpiredEntries(DateTimeOffset now)
    {
        while (_insertionOrder.TryPeek(out var oldest))
        {
            if (!_entries.TryGetValue(oldest, out var entry))
            {
                _insertionOrder.Dequeue();
                continue;
            }

            if (entry.ExpiresAt > now && _entries.Count <= MaxEntries)
                break;

            _insertionOrder.Dequeue();
            _entries.Remove(oldest);
        }
    }
}
