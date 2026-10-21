namespace DonateWeb.Services;

public sealed class SupportAiOptions
{
    public const string SectionName = "SupportAi";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5.4-mini";

    // Giới hạn token đầu vào + đầu ra mỗi tháng theo UTC. Mặc định an toàn để thử nghiệm.
    public long MonthlyTokenLimit { get; set; } = 100_000;
    public int MaxOutputTokens { get; set; } = 500;
    public int MaxHistoryMessages { get; set; } = 10;
}
