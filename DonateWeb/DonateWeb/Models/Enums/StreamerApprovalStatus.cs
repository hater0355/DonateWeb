namespace DonateWeb.Models.Enums
{
    /// <summary>
    /// Trạng thái phê duyệt hồ sơ Streamer bởi Quản trị viên (Admin)
    /// </summary>
    public enum StreamerApprovalStatus
    {
        /// <summary>
        /// 0: Đang chờ Quản trị viên xét duyệt (Sau khi người dùng gửi đơn đăng ký)
        /// </summary>
        Pending = 0,

        /// <summary>
        /// 1: Đã được Quản trị viên phê duyệt chính thức (Được cấp quyền Streamer và đổi mã ID đầu 9 thành 1)
        /// </summary>
        Approved = 1,

        /// <summary>
        /// 2: Bị Quản trị viên từ chối phê duyệt (Kèm lý do từ chối)
        /// </summary>
        Rejected = 2
    }
}
