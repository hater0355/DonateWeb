using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.Uploads;
using DonateWeb.ViewModels.Shop;

namespace DonateWeb.Controllers
{
    public class ShopController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ShopController(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        /// <summary>
        /// 1. SÀN SẢN PHẨM / SHOP CHUNG CỦA TẤT CẢ CÁC STREAMER
        /// Hiển thị toàn bộ sản phẩm của các streamer đã đưa lên sàn, có ghi rõ từ streamer nào và thanh tìm kiếm
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(string? keyword, string? streamerSlug, string? sortBy)
        {
            var query = _context.ShopProducts
                .Include(p => p.StreamerProfile)
                    .ThenInclude(sp => sp.User)
                .Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved && p.StreamerProfile.IsActive)
                .AsNoTracking();

            // Lọc theo từ khóa tìm kiếm (tên sản phẩm, mô tả, tên hiển thị streamer)
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(kw) ||
                                         (p.Description != null && p.Description.ToLower().Contains(kw)) ||
                                         p.StreamerProfile.DisplayName.ToLower().Contains(kw));
            }

            // Lọc theo Streamer cụ thể
            if (!string.IsNullOrWhiteSpace(streamerSlug))
            {
                var sSlug = streamerSlug.Trim().ToLower();
                query = query.Where(p => p.StreamerProfile.Slug.ToLower() == sSlug ||
                                         (p.StreamerProfile.User != null && p.StreamerProfile.User.AccountId != null && p.StreamerProfile.User.AccountId.ToLower() == sSlug));
            }

            // Sắp xếp
            query = sortBy switch
            {
                "price_asc" => query.OrderBy(p => p.Price),
                "price_desc" => query.OrderByDescending(p => p.Price),
                _ => query.OrderByDescending(p => p.CreatedAt) // Mới nhất
            };

            var products = await query.Select(p => new ShopProductCardViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                ImageUrl = string.IsNullOrWhiteSpace(p.ImageUrl) ? "/images/default-banner.jpg" : p.ImageUrl,
                StockQuantity = p.StockQuantity,
                StreamerProfileId = p.StreamerProfileId,
                StreamerName = p.StreamerProfile.DisplayName,
                StreamerSlug = !string.IsNullOrWhiteSpace(p.StreamerProfile.User != null ? p.StreamerProfile.User.AccountId : null) ? p.StreamerProfile.User!.AccountId! : p.StreamerProfile.Slug,
                StreamerAvatar = string.IsNullOrWhiteSpace(p.StreamerProfile.AvatarUrl) ? "/images/default-avatar.png" : p.StreamerProfile.AvatarUrl,
                StreamerRank = p.StreamerProfile.Rank,
                IsVerified = p.StreamerProfile.IsVerified
            }).ToListAsync();

            // Danh sách streamer để hiển thị bộ lọc
            var activeStreamers = await _context.StreamerProfiles
                .Where(s => s.IsActive && s.ShopProducts.Any(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved))
                .Select(s => new StreamerSelectOptionViewModel
                {
                    Slug = !string.IsNullOrWhiteSpace(s.User != null ? s.User.AccountId : null) ? s.User!.AccountId! : s.Slug,
                    DisplayName = s.DisplayName
                })
                .Distinct()
                .ToListAsync();

            var viewModel = new ShopIndexViewModel
            {
                Products = products,
                SearchKeyword = keyword?.Trim(),
                SelectedStreamerSlug = streamerSlug?.Trim(),
                SortBy = sortBy,
                Streamers = activeStreamers,
                TotalProducts = products.Count
            };

            return View(viewModel);
        }

        /// <summary>
        /// 2. GIAN HÀNG & GIỎ HÀNG RIÊNG BIỆT CỦA TỪNG STREAMER
        /// Mỗi streamer có một giỏ hàng/gian hàng độc lập hoàn toàn
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> StreamerShop(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return RedirectToAction(nameof(Index));
            }

            var cleanSlug = slug.Trim();
            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                .Include(s => s.ShopProducts.Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved))
                .FirstOrDefaultAsync(s => s.Slug == cleanSlug || (s.User != null && s.User.AccountId == cleanSlug));

            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy gian hàng của Streamer này.";
                return RedirectToAction(nameof(Index));
            }

            bool isOwner = false;
            decimal viewerBalance = 0;
            string? viewerName = null;
            string? viewerPhone = null;

            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                isOwner = (streamer.UserId == uid);
                var currentUser = await _context.Users.FindAsync(uid);
                if (currentUser != null)
                {
                    viewerBalance = currentUser.WalletBalance;
                    viewerName = currentUser.FullName ?? currentUser.Username;
                    viewerPhone = currentUser.PhoneNumber;
                }
            }

            var streamerIdentifier = !string.IsNullOrWhiteSpace(streamer.User?.AccountId) ? streamer.User.AccountId : streamer.Slug;

            var viewModel = new StreamerShopViewModel
            {
                StreamerId = streamer.Id,
                StreamerName = streamer.DisplayName,
                StreamerSlug = streamerIdentifier,
                StreamerAvatar = string.IsNullOrWhiteSpace(streamer.AvatarUrl) ? "/images/default-avatar.png" : streamer.AvatarUrl,
                StreamerBanner = string.IsNullOrWhiteSpace(streamer.BannerUrl) ? "/images/default-banner.jpg" : streamer.BannerUrl,
                BankName = streamer.BankName,
                BankAccountNumber = streamer.BankAccountNumber,
                BankAccountName = streamer.BankAccountName ?? streamer.DisplayName,
                IsOwner = isOwner,
                Products = streamer.ShopProducts.OrderByDescending(p => p.CreatedAt).ToList(),
                ViewerWalletBalance = viewerBalance,
                IsViewerAuthenticated = User.Identity?.IsAuthenticated == true,
                ViewerName = viewerName,
                ViewerPhone = viewerPhone
            };

            return View(viewModel);
        }

        /// <summary>
        /// 3. XỬ LÝ ĐẶT HÀNG & THANH TOÁN (HỖ TRỢ THANH TOÁN BẰNG VÍ HOẶC QUÉT MÃ VIETQR)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequestViewModel model)
        {
            if (model == null || model.Items == null || !model.Items.Any())
            {
                return Json(new { success = false, message = "Giỏ hàng của bạn đang trống." });
            }

            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == model.StreamerProfileId);

            if (streamer == null)
            {
                return Json(new { success = false, message = "Không tìm thấy thông tin Streamer bán hàng." });
            }

            // Lấy danh sách sản phẩm hợp lệ trong database
            var productIds = model.Items.Select(i => i.ProductId).ToList();
            var products = await _context.ShopProducts
                .Where(p => productIds.Contains(p.Id) && p.StreamerProfileId == streamer.Id && p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved)
                .ToListAsync();

            if (!products.Any())
            {
                return Json(new { success = false, message = "Các sản phẩm trong giỏ không hợp lệ hoặc đã ngừng bán." });
            }

            decimal totalAmount = 0;
            var orderItems = new List<ShopOrderItem>();

            foreach (var item in model.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (prod != null && item.Quantity > 0)
                {
                    totalAmount += prod.Price * item.Quantity;
                    orderItems.Add(new ShopOrderItem
                    {
                        ShopProductId = prod.Id,
                        ProductName = prod.Name,
                        Price = prod.Price,
                        Quantity = item.Quantity
                    });
                }
            }

            if (totalAmount <= 0)
            {
                return Json(new { success = false, message = "Tổng tiền đơn hàng không hợp lệ." });
            }

            int? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                currentUserId = uid;
            }

            var orderCode = $"ORD{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";
            var paymentMethod = string.Equals(model.PaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase) ? "Wallet" : "VietQR";

            // XỬ LÝ PHƯƠNG THỨC 1: THANH TOÁN BẰNG TIỀN TRONG VÍ
            if (paymentMethod == "Wallet")
            {
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập tài khoản để thanh toán bằng số dư Ví!" });
                }

                var buyer = await _context.Users.FindAsync(currentUserId.Value);
                if (buyer == null)
                {
                    return Json(new { success = false, message = "Tài khoản người mua không hợp lệ." });
                }

                if (buyer.WalletBalance < totalAmount)
                {
                    return Json(new { 
                        success = false, 
                        message = $"Số dư ví của bạn không đủ ({buyer.WalletBalance:N0} VNĐ) để thanh toán đơn hàng {totalAmount:N0} VNĐ. Vui lòng nạp thêm tiền hoặc chọn thanh toán quét mã QR!" 
                    });
                }

                // Trừ tiền người mua
                buyer.WalletBalance -= totalAmount;

                // Cộng tiền doanh thu cho Streamer
                var streamerUser = await _context.Users.FindAsync(streamer.UserId);
                if (streamerUser != null)
                {
                    streamerUser.WalletBalance += totalAmount;
                }

                // Ghi nhận lịch sử giao dịch ví cho Người mua
                _context.WalletTransactions.Add(new WalletTransaction
                {
                    UserId = buyer.Id,
                    TransactionCode = $"WTX-{orderCode}-BUY",
                    TransactionType = "PURCHASE",
                    Amount = totalAmount,
                    BalanceBefore = buyer.WalletBalance + totalAmount,
                    BalanceAfter = buyer.WalletBalance,
                    PaymentMethodName = "Ví số dư OnlyFan",
                    Status = WalletTransaction.StatusCompleted,
                    Note = $"Thanh toán đơn hàng #{orderCode} tại Shop của {streamer.DisplayName}",
                    CreatedAt = DateTime.UtcNow
                });

                // Ghi nhận lịch sử doanh thu cho Streamer
                if (streamerUser != null)
                {
                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        UserId = streamerUser.Id,
                        TransactionCode = $"WTX-{orderCode}-REV",
                        TransactionType = "SHOP_SALE",
                        Amount = totalAmount,
                        BalanceBefore = streamerUser.WalletBalance - totalAmount,
                        BalanceAfter = streamerUser.WalletBalance,
                        PaymentMethodName = "Ví số dư OnlyFan",
                        Status = WalletTransaction.StatusCompleted,
                        Note = $"Doanh thu bán hàng đơn #{orderCode} từ khách {model.BuyerName.Trim()}",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Tạo đơn hàng trạng thái ĐÃ THANH TOÁN
                var paidOrder = new ShopOrder
                {
                    OrderCode = orderCode,
                    StreamerProfileId = streamer.Id,
                    BuyerUserId = buyer.Id,
                    BuyerName = string.IsNullOrWhiteSpace(model.BuyerName) ? buyer.FullName ?? buyer.Username : model.BuyerName.Trim(),
                    BuyerPhone = model.BuyerPhone?.Trim(),
                    BuyerAddress = model.BuyerAddress?.Trim(),
                    Note = model.Note?.Trim(),
                    TotalAmount = totalAmount,
                    PaymentMethod = "Wallet",
                    PaymentStatus = "Paid",
                    TransactionCode = $"TX-WALLET-{orderCode}",
                    CreatedAt = DateTime.UtcNow,
                    PaidAt = DateTime.UtcNow
                };

                foreach (var oi in orderItems)
                {
                    paidOrder.Items.Add(oi);
                }

                _context.ShopOrders.Add(paidOrder);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    orderCode = paidOrder.OrderCode,
                    isPaid = true,
                    message = "Thanh toán bằng số dư Ví thành công! Đơn hàng đã được ghi nhận."
                });
            }

            // XỬ LÝ PHƯƠNG THỨC 2: THANH TOÁN BẰNG QUÉT MÃ VIETQR
            var pendingOrder = new ShopOrder
            {
                OrderCode = orderCode,
                StreamerProfileId = streamer.Id,
                BuyerUserId = currentUserId,
                BuyerName = model.BuyerName.Trim(),
                BuyerPhone = model.BuyerPhone?.Trim(),
                BuyerAddress = model.BuyerAddress?.Trim(),
                Note = model.Note?.Trim(),
                TotalAmount = totalAmount,
                PaymentMethod = "VietQR",
                PaymentStatus = "Pending",
                TransactionCode = $"QR-{orderCode}",
                CreatedAt = DateTime.UtcNow
            };

            foreach (var oi in orderItems)
            {
                pendingOrder.Items.Add(oi);
            }

            _context.ShopOrders.Add(pendingOrder);
            await _context.SaveChangesAsync();

            // Sinh mã QR VietQR cho đơn hàng
            var transferContent = $"SHOP {orderCode}";
            var qrBase64 = GeneratePaymentQrBase64(
                streamer.BankName,
                streamer.BankAccountNumber,
                streamer.BankAccountName ?? streamer.DisplayName,
                totalAmount,
                transferContent);

            return Json(new
            {
                success = true,
                orderCode = pendingOrder.OrderCode,
                isPaid = false,
                qrBase64 = qrBase64,
                bankName = string.IsNullOrWhiteSpace(streamer.BankName) ? "MB Bank" : streamer.BankName,
                bankAccountNumber = string.IsNullOrWhiteSpace(streamer.BankAccountNumber) ? "0354031024" : streamer.BankAccountNumber,
                bankAccountName = string.IsNullOrWhiteSpace(streamer.BankAccountName) ? streamer.DisplayName.ToUpper() : streamer.BankAccountName.ToUpper(),
                totalAmount = totalAmount,
                formattedAmount = totalAmount.ToString("N0") + " VNĐ",
                transferContent = transferContent,
                message = "Tạo đơn hàng thành công! Quét mã QR để hoàn tất thanh toán."
            });
        }

        /// <summary>
        /// 4. XEM CHI TIẾT ĐƠN HÀNG
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Detail(string orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                return RedirectToAction(nameof(Index));
            }

            var order = await _context.ShopOrders
                .Include(o => o.StreamerProfile)
                    .ThenInclude(s => s.User)
                .Include(o => o.Items)
                    .ThenInclude(i => i.ShopProduct)
                .FirstOrDefaultAsync(o => o.OrderCode == orderCode.Trim());

            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy thông tin đơn hàng.";
                return RedirectToAction(nameof(Index));
            }

            return View(order);
        }

        /// <summary>
        /// 5. LỊCH SỬ ĐƠN HÀNG (CỦA VIEWER VÀ STREAMER)
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> History()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var isStreamer = User.IsInRole(UserRoles.Streamer);
            var myPurchases = await _context.ShopOrders
                .Include(o => o.StreamerProfile)
                .Include(o => o.Items)
                .Where(o => o.BuyerUserId == uid)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            List<ShopOrder> streamerReceivedOrders = new();
            if (isStreamer)
            {
                var streamer = await _context.StreamerProfiles.FirstOrDefaultAsync(s => s.UserId == uid);
                if (streamer != null)
                {
                    streamerReceivedOrders = await _context.ShopOrders
                        .Include(o => o.Items)
                        .Where(o => o.StreamerProfileId == streamer.Id)
                        .OrderByDescending(o => o.CreatedAt)
                        .ToListAsync();
                }
            }

            ViewBag.Purchases = myPurchases;
            ViewBag.StreamerOrders = streamerReceivedOrders;
            ViewBag.IsStreamer = isStreamer;

            return View();
        }

        /// <summary>
        /// 6. STREAMER QUẢN LÝ SẢN PHẨM (THÊM / SỬA / XÓA)
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpGet]
        public async Task<IActionResult> MyProducts(string? status)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _context.StreamerProfiles
                .Include(s => s.ShopProducts)
                .FirstOrDefaultAsync(s => s.UserId == uid);

            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Bạn chưa có hồ sơ Streamer.";
                return RedirectToAction("Register", "Streamer");
            }

            var allProducts = streamer.ShopProducts.ToList();
            var pendingCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Pending);
            var approvedCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Approved);
            var rejectedCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Rejected);

            var filteredProducts = allProducts.AsEnumerable();
            var currentFilter = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLower();

            filteredProducts = currentFilter switch
            {
                "pending" => filteredProducts.Where(p => p.ApprovalStatus == ProductApprovalStatus.Pending),
                "approved" => filteredProducts.Where(p => p.ApprovalStatus == ProductApprovalStatus.Approved),
                "rejected" => filteredProducts.Where(p => p.ApprovalStatus == ProductApprovalStatus.Rejected),
                _ => filteredProducts
            };

            var model = new ProductManageViewModel
            {
                StreamerProfileId = streamer.Id,
                StreamerName = streamer.DisplayName,
                Products = filteredProducts.OrderByDescending(p => p.CreatedAt).ToList(),
                NewProduct = new ProductInputModel(),
                PendingCount = pendingCount,
                ApprovedCount = approvedCount,
                RejectedCount = rejectedCount,
                StatusFilter = currentFilter
            };

            return View(model);
        }

        /// <summary>
        /// 7. STREAMER THÊM MỚI SẢN PHẨM (HỖ TRỢ CHỌN ẢNH TỪ MÁY HOẶC NHẬP URL)
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProduct(ProductInputModel model)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _context.StreamerProfiles.FirstOrDefaultAsync(s => s.UserId == uid);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy hồ sơ Streamer.";
                return RedirectToAction("Register", "Streamer");
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Dữ liệu sản phẩm chưa hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToAction(nameof(MyProducts));
            }

            string? finalImageUrl = model.ImageUrl?.Trim();

            // Nếu người dùng chọn tải ảnh lên từ máy tính
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                var (uploadedPath, error) = await ProductUploadService.SaveAsync(
                    model.ImageFile, 
                    _webHostEnvironment.WebRootPath, 
                    streamer.Id, 
                    HttpContext.RequestAborted);

                if (error != null)
                {
                    TempData["ErrorMessage"] = error;
                    return RedirectToAction(nameof(MyProducts));
                }

                if (!string.IsNullOrWhiteSpace(uploadedPath))
                {
                    finalImageUrl = uploadedPath;
                }
            }

            if (string.IsNullOrWhiteSpace(finalImageUrl))
            {
                finalImageUrl = "https://images.unsplash.com/photo-1521572267360-ee0c2909d518?q=80&w=800&auto=format&fit=crop";
            }

            var product = new ShopProduct
            {
                StreamerProfileId = streamer.Id,
                Name = model.Name.Trim(),
                Price = model.Price,
                Description = model.Description?.Trim(),
                ImageUrl = finalImageUrl,
                StockQuantity = model.StockQuantity > 0 ? model.StockQuantity : 999,
                IsActive = true,
                ApprovalStatus = ProductApprovalStatus.Pending, // Chờ duyệt
                CreatedAt = DateTime.UtcNow
            };

            _context.ShopProducts.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã gửi sản phẩm \"{product.Name}\" thành công! Sản phẩm đang chờ Quản trị viên (Admin) xét duyệt trước khi hiển thị mở bán.";
            return RedirectToAction(nameof(MyProducts));
        }

        /// <summary>
        /// 8. STREAMER XÓA SẢN PHẨM
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _context.StreamerProfiles.FirstOrDefaultAsync(s => s.UserId == uid);
            if (streamer == null) return RedirectToAction("Login", "Auth");

            var product = await _context.ShopProducts.FirstOrDefaultAsync(p => p.Id == id && p.StreamerProfileId == streamer.Id);
            if (product != null)
            {
                _context.ShopProducts.Remove(product);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xóa sản phẩm khỏi gian hàng.";
            }

            return RedirectToAction(nameof(MyProducts));
        }

        /// <summary>
        /// Sinh mã QR VietQR Napas247 tương thích
        /// </summary>
        private static string GeneratePaymentQrBase64(string? bankName, string? bankAccountNo, string? bankAccountName, decimal amount, string transferContent)
        {
            var bank = string.IsNullOrWhiteSpace(bankName) ? "MB Bank" : bankName.Trim();
            var accNo = string.IsNullOrWhiteSpace(bankAccountNo) ? "0354031024" : bankAccountNo.Trim();
            var accName = string.IsNullOrWhiteSpace(bankAccountName) ? "STREAMER SHOP" : bankAccountName.Trim().ToUpper();

            var qrPayload = $"BANK:{bank}|ACC:{accNo}|NAME:{accName}|AMOUNT:{amount:0}|CONTENT:{transferContent}";

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(10);

            return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
        }
    }
}
