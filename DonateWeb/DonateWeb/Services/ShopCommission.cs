namespace DonateWeb.Services;

/// <summary>
/// Phí nền tảng trên doanh thu đơn hàng Shop. Streamer nhận 70% số tiền khách
/// thực trả sau khi áp dụng mã giảm giá; nền tảng giữ 30%.
/// </summary>
public static class ShopCommission
{
    public const decimal PlatformRate = 0.30m;

    public static decimal StreamerNet(decimal grossAmount)
    {
        if (grossAmount <= 0)
            return 0;

        var net = Math.Round(grossAmount * (1m - PlatformRate), 0, MidpointRounding.AwayFromZero);
        return Math.Min(grossAmount, net);
    }

    public static decimal PlatformFee(decimal grossAmount) =>
        Math.Max(0, grossAmount - StreamerNet(grossAmount));
}
