namespace DonateWeb.Models.Enums
{
    /// <summary>
    /// Trạng thái phê duyệt sản phẩm trên gian hàng bởi Quản trị viên (Admin)
    /// </summary>
    public enum ProductApprovalStatus
    {
        /// <summary>
        /// 0: Đang chờ Quản trị viên xét duyệt (Sau khi Streamer thêm sản phẩm mới)
        /// </summary>
        Pending = 0,

        /// <summary>
        /// 1: Đã được Quản trị viên phê duyệt chính thức (Sản phẩm được bày bán công khai trên sàn)
        /// </summary>
        Approved = 1,

        /// <summary>
        /// 2: Bị Quản trị viên từ chối phê duyệt (Kèm lý do từ chối)
        /// </summary>
        Rejected = 2
    }
}
