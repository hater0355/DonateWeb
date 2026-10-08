using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DonateWeb.Areas.Admin.Services;
using DonateWeb.Areas.Admin.ViewModels;
using DonateWeb.Models.Enums;

namespace DonateWeb.Areas.Admin.Controllers
{
    /// <summary>
    /// Controller trung tâm của Phân hệ Quản trị (Admin Panel).
    /// Được bảo vệ nghiêm ngặt bằng thuộc tính [Authorize(Roles = UserRoles.Admin)],
    /// đảm bảo chỉ có tài khoản Quản trị viên mới được phép truy cập.
    /// </summary>
    [Area("Admin")]
    [Authorize(Roles = UserRoles.Admin)]
    public class AdminController : Controller
    {
        private readonly IAdminService _adminService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(IAdminService adminService, ILogger<AdminController> logger)
        {
            _adminService = adminService;
            _logger = logger;
        }

        // ====================================================================
        // 1. CHỨC NĂNG: GIÁM SÁT DÒNG TIỀN RA / VÀO HỆ THỐNG
        // ====================================================================

        /// <summary>
        /// Trang Dashboard tổng quan: Giám sát toàn bộ dòng tiền vào/ra, số dư hệ thống,
        /// biểu đồ 7 ngày gần nhất, top streamers và top donors.
        /// URL: /Admin hoặc /Admin/Index
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var dashboardData = await _adminService.GetCashFlowDashboardAsync();
            return View(dashboardData);
        }


        // ====================================================================
        // 2. CHỨC NĂNG: QUẢN LÝ TRẠNG THÁI STREAMER (KHÓA / KÍCH HOẠT KÊNH)
        // ====================================================================

        /// <summary>
        /// Trang danh sách Streamer: Tra cứu, xem trạng thái hoạt động và tích xanh.
        /// URL: /Admin/Streamers
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Streamers(
            string? search,
            string statusFilter = "all",
            string verificationFilter = "all")
        {
            var streamersData = await _adminService.GetStreamersAsync(search, statusFilter, verificationFilter);
            return View(streamersData);
        }

        /// <summary>
        /// Phê duyệt hồ sơ đăng ký làm Streamer: cấp quyền Streamer và đổi mã ID đầu 9 thành 1
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveStreamer(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.ApproveStreamerAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Streamers));
        }

        /// <summary>
        /// Từ chối hồ sơ đăng ký làm Streamer kèm lý do
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectStreamer(int id, string? reason)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.RejectStreamerAsync(id, reason ?? "", adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Streamers));
        }

        /// <summary>
        /// Xử lý khóa kênh Streamer vi phạm chính sách cộng đồng
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LockStreamer(int id, string reason)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.LockStreamerAsync(id, reason, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Streamers));
        }

        /// <summary>
        /// Xử lý kích hoạt mở khóa lại kênh Streamer
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlockStreamer(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.UnlockStreamerAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Streamers));
        }

        /// <summary>
        /// Cấp hoặc gỡ huy hiệu xác minh (tích xanh) cho Streamer
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleVerify(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.ToggleVerifyStreamerAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Streamers));
        }

        /// <summary>
        /// Xóa streamer khỏi hệ thống (chuyển về Viewer hoặc xóa luôn tài khoản)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStreamer(int id, bool deleteUserAccount = false)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.DeleteStreamerAsync(id, adminUser, deleteUserAccount);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Streamers));
        }


        // ====================================================================
        // 3. CHỨC NĂNG: TRA CỨU LOG GIAO DỊCH & XỬ LÝ KHIẾU NẠI (DISPUTE)
        // ====================================================================

        /// <summary>
        /// Trang tra cứu và kiểm tra lịch sử giao dịch toàn hệ thống.
        /// Cho phép lọc theo mã GD, người gửi, streamer, trạng thái và cờ khiếu nại.
        /// URL: /Admin/Transactions
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Transactions(
            string? search,
            int? status,
            int? paymentMethod,
            bool? isDisputed,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1)
        {
            var transactionsData = await _adminService.GetTransactionsAsync(
                search,
                status,
                paymentMethod,
                isDisputed,
                fromDate,
                toDate,
                page,
                pageSize: 15);

            return View(transactionsData);
        }

        /// <summary>
        /// Trang xem chi tiết một giao dịch cụ thể, kèm dòng thời gian nhật ký kiểm tra (Audit Logs)
        /// và biểu mẫu xử lý khiếu nại (Hoàn tiền / Chấp thuận / Bác bỏ).
        /// URL: /Admin/TransactionDetail/{id}
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> TransactionDetail(int id)
        {
            var detail = await _adminService.GetTransactionDetailAsync(id);
            if (detail == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy thông tin giao dịch yêu cầu.";
                return RedirectToAction(nameof(Transactions));
            }

            return View(detail);
        }

        /// <summary>
        /// Xử lý quyết định phân xử khiếu nại hoặc thay đổi trạng thái giao dịch
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveDispute(int id, int newStatus, string actionType, string note)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.ResolveDisputeOrChangeStatusAsync(
                id,
                (DonationStatus)newStatus,
                actionType,
                note,
                adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(TransactionDetail), new { id });
        }


        // ====================================================================
        // 4. CHỨC NĂNG: QUẢN LÝ VÀ PHÊ DUYỆT SẢN PHẨM SHOP STREAMER
        // ====================================================================

        /// <summary>
        /// Trang danh sách sản phẩm do streamer đăng bán: Kiểm duyệt nội dung, giá, ảnh trước khi hiển thị công khai.
        /// URL: /Admin/Products
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Products(
            string? search,
            string statusFilter = "all",
            int? streamerId = null,
            int page = 1)
        {
            var productsData = await _adminService.GetProductsAsync(search, statusFilter, streamerId, page, pageSize: 15);
            return View(productsData);
        }

        /// <summary>
        /// Phê duyệt sản phẩm của Streamer
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveProduct(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.ApproveProductAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Products));
        }

        /// <summary>
        /// Từ chối sản phẩm của Streamer kèm lý do
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectProduct(int id, string? reason)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.RejectProductAsync(id, reason ?? "", adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Products));
        }

        /// <summary>
        /// Xóa sản phẩm vi phạm
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.DeleteProductAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Products));
        }

        /// <summary>
        /// Bật/tắt trạng thái mở bán sản phẩm
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleProductActive(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.ToggleProductActiveAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Products));
        }


        // ====================================================================
        // 5. CHỨC NĂNG: ĐĂNG & QUẢN LÝ BẢN TIN BREAKING NEWS
        // ====================================================================

        /// <summary>
        /// Trang đăng và quản lý Breaking News (tin tức chạy chữ ngang trên toàn hệ thống).
        /// URL: /Admin/BreakingNews
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> BreakingNews()
        {
            var model = await _adminService.GetBreakingNewsManageAsync();
            return View(model);
        }

        /// <summary>
        /// Lưu hoặc đăng mới Breaking News
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveBreakingNews(BreakingNewsInputModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Dữ liệu bản tin chưa hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToAction(nameof(BreakingNews));
            }

            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.SaveBreakingNewsAsync(model, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(BreakingNews));
        }

        /// <summary>
        /// Bật/Tắt hiển thị một bản tin
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBreakingNews(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.ToggleBreakingNewsAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(BreakingNews));
        }

        /// <summary>
        /// Xóa bản tin
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBreakingNews(int id)
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.DeleteBreakingNewsAsync(id, adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(BreakingNews));
        }

        /// <summary>
        /// Gỡ bỏ toàn bộ tin tức (đưa thanh Breaking News về trạng thái để trống)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearActiveBreakingNews()
        {
            var adminUser = User.Identity?.Name ?? "Admin";
            var (success, message) = await _adminService.ClearActiveBreakingNewsAsync(adminUser);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(BreakingNews));
        }
    }
}
