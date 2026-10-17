namespace DonateWeb.Services;

/// <summary>
/// Quy tắc chia khoản donate: nền tảng giữ 10%,
/// streamer nhận phần còn lại. Tiền được làm tròn đến đồng Việt Nam cho từng giao dịch.
/// </summary>
public static class DonationCommission
{
    public const decimal PlatformRate = 0.10m;

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
