using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DonateWeb.Services;

public sealed class GhnOptions
{
    public string Token { get; set; } = string.Empty;
    public int ShopId { get; set; }
}

public sealed record GhnMasterDataItem(int Id, string Name);

public sealed record GhnShippingItem(string Name, int Quantity, long Price, int WeightGrams, int LengthCm, int WidthCm, int HeightCm);

public sealed record GhnFeePreviewInput(
    string RecipientName,
    string RecipientPhone,
    string RecipientAddress,
    string WardName,
    string ProvinceName,
    int WeightGrams,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    int ServiceTypeId,
    IReadOnlyList<GhnShippingItem> Items);

public sealed record GhnFeePreviewResult(long TotalFee, DateTimeOffset? ExpectedDeliveryTime);

public sealed class GhnShippingException(string message) : Exception(message);

public interface IGhnShippingService
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<GhnMasterDataItem>> GetProvincesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GhnMasterDataItem>> GetWardsAsync(int provinceId, CancellationToken cancellationToken = default);
    Task<GhnFeePreviewResult> PreviewFeeAsync(GhnFeePreviewInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chỉ gọi các endpoint GHN staging để xem danh mục và xem trước phí.
/// Lớp này tuyệt đối không gọi endpoint tạo vận đơn.
/// </summary>
public sealed class GhnShippingService : IGhnShippingService
{
    private const string StagingBaseUrl = "https://dev-online-gateway.ghn.vn/shiip/public-api/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly GhnOptions _options;

    public GhnShippingService(HttpClient httpClient, IMemoryCache cache, IOptions<GhnOptions> options)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(StagingBaseUrl);
        _cache = cache;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Token) && _options.ShopId > 0;

    public Task<IReadOnlyList<GhnMasterDataItem>> GetProvincesAsync(CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync("ghn-staging-provinces", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            var response = await GetAsync<GhnProvinceDto[]>("v3/master-data/province/all?offset=0&limit=200", cancellationToken);
            return (IReadOnlyList<GhnMasterDataItem>)(response ?? Array.Empty<GhnProvinceDto>())
                .Where(item => item.Status == 1 && item.Id > 0 && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name)
                .Select(item => new GhnMasterDataItem(item.Id, item.Name))
                .ToArray();
        })!;

    public Task<IReadOnlyList<GhnMasterDataItem>> GetWardsAsync(int provinceId, CancellationToken cancellationToken = default) =>
        _cache.GetOrCreateAsync($"ghn-staging-wards-{provinceId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            var response = await GetAsync<GhnWardDto[]>(
                $"v3/master-data/ward/all-by-province-id?province_id={provinceId}&offset=0&limit=200", cancellationToken);
            return (IReadOnlyList<GhnMasterDataItem>)(response ?? Array.Empty<GhnWardDto>())
                .Where(item => item.Status == 1 && item.Id > 0 && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name)
                .Select(item => new GhnMasterDataItem(item.Id, item.Name))
                .ToArray();
        })!;

    public async Task<GhnFeePreviewResult> PreviewFeeAsync(GhnFeePreviewInput input, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var requestBody = new GhnPreviewRequest
        {
            PaymentTypeId = 2, // Người nhận trả phí vận chuyển; chỉ dùng để GHN tính báo giá.
            RequiredNote = "KHONGCHOXEMHANG",
            RecipientName = input.RecipientName,
            RecipientPhone = input.RecipientPhone,
            RecipientAddress = input.RecipientAddress,
            RecipientWardName = input.WardName,
            RecipientProvinceName = input.ProvinceName,
            IsNewRecipientAddress = true,
            Weight = input.WeightGrams,
            Length = input.LengthCm,
            Width = input.WidthCm,
            Height = input.HeightCm,
            ServiceTypeId = input.ServiceTypeId,
            Items = input.Items.Select(item => new GhnPreviewItem
            {
                Name = item.Name,
                Quantity = item.Quantity,
                Price = item.Price,
                Weight = item.WeightGrams,
                Length = item.LengthCm,
                Width = item.WidthCm,
                Height = item.HeightCm
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        using var request = CreateRequest(HttpMethod.Post, "v2/shipping-order/preview");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await SendAsync<GhnPreviewData>(request, cancellationToken);

        // GHN tài liệu hóa order_code rỗng cho preview. Dừng nếu phản hồi có mã vận đơn.
        if (response == null)
        {
            throw new GhnShippingException("GHN không trả dữ liệu xem trước phí.");
        }
        if (!string.IsNullOrWhiteSpace(response.OrderCode))
        {
            throw new GhnShippingException("GHN trả về mã vận đơn ngoài dự kiến. Đã dừng hiển thị kết quả để kiểm tra cấu hình.");
        }

        if (response.TotalFee < 0)
        {
            throw new GhnShippingException("GHN không trả về mức phí hợp lệ.");
        }

        return new GhnFeePreviewResult(response.TotalFee, response.ExpectedDeliveryTime);
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateRequest(HttpMethod.Get, relativeUrl);
        return await SendAsync<T>(request, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        EnsureConfigured();
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.TryAddWithoutValidation("Token", _options.Token);
        request.Headers.TryAddWithoutValidation("ShopId", _options.ShopId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GhnShippingException("GHN phản hồi quá thời gian chờ. Vui lòng thử lại sau.");
        }
        catch (HttpRequestException)
        {
            throw new GhnShippingException("Không kết nối được đến GHN staging. Vui lòng thử lại sau.");
        }

        using (httpResponse)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            GhnApiResponse<T>? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<GhnApiResponse<T>>(body, JsonOptions);
            }
            catch (JsonException)
            {
                throw new GhnShippingException("GHN trả về dữ liệu không đọc được.");
            }

            if (!httpResponse.IsSuccessStatusCode || envelope == null || envelope.Code != 200 || envelope.Data == null)
            {
                var message = string.IsNullOrWhiteSpace(envelope?.Message)
                    ? "GHN chưa chấp nhận yêu cầu. Hãy kiểm tra Token, ShopId và địa chỉ kho lấy hàng trên GHN."
                    : envelope.Message;
                throw new GhnShippingException(message);
            }

            return envelope.Data;
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new GhnShippingException("Chưa cấu hình GHN staging. Quản trị viên cần thêm GHN_TOKEN và GHN_SHOP_ID vào cấu hình môi trường.");
        }
    }

    private sealed class GhnApiResponse<T>
    {
        public int Code { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    private sealed class GhnProvinceDto
    {
        [JsonPropertyName("_id")] public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Status { get; set; }
    }

    private sealed class GhnWardDto
    {
        [JsonPropertyName("_id")] public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Status { get; set; }
    }

    private sealed class GhnPreviewRequest
    {
        [JsonPropertyName("payment_type_id")] public int PaymentTypeId { get; set; }
        [JsonPropertyName("required_note")] public string RequiredNote { get; set; } = string.Empty;
        [JsonPropertyName("to_name")] public string RecipientName { get; set; } = string.Empty;
        [JsonPropertyName("to_phone")] public string RecipientPhone { get; set; } = string.Empty;
        [JsonPropertyName("to_address")] public string RecipientAddress { get; set; } = string.Empty;
        [JsonPropertyName("to_ward_name")] public string RecipientWardName { get; set; } = string.Empty;
        [JsonPropertyName("to_province_name")] public string RecipientProvinceName { get; set; } = string.Empty;
        [JsonPropertyName("is_new_to_address")] public bool IsNewRecipientAddress { get; set; }
        [JsonPropertyName("weight")] public int Weight { get; set; }
        [JsonPropertyName("length")] public int Length { get; set; }
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
        [JsonPropertyName("service_type_id")] public int ServiceTypeId { get; set; }
        [JsonPropertyName("items")] public GhnPreviewItem[] Items { get; set; } = [];
    }

    private sealed class GhnPreviewItem
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("price")] public long Price { get; set; }
        [JsonPropertyName("weight")] public int Weight { get; set; }
        [JsonPropertyName("length")] public int Length { get; set; }
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
    }

    private sealed class GhnPreviewData
    {
        [JsonPropertyName("order_code")] public string? OrderCode { get; set; }
        [JsonPropertyName("total_fee")] public long TotalFee { get; set; }
        [JsonPropertyName("expected_delivery_time")] public DateTimeOffset? ExpectedDeliveryTime { get; set; }
    }
}
