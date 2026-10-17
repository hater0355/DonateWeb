using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;

namespace DonateWeb.Controllers
{
    /// <summary>
    /// Quản lý các mã giảm giá dùng tại Shop. Chỉ Admin được tạo hoặc bật/tắt mã.
    /// </summary>
    [Authorize(Roles = UserRoles.Admin)]
    public class CouponsController : Controller
    {
        private readonly AppDbContext _context;

        public CouponsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var coupons = await _context.DiscountCoupons
                .AsNoTracking()
                .OrderByDescending(coupon => coupon.CreatedAtUtc)
                .ToListAsync();

            return View(coupons);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string? code,
            string? content,
            string? provider,
            string? discountType,
            decimal discountValue,
            DateTime? expiresOn)
        {
            var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
            var normalizedType = (discountType ?? string.Empty).Trim();
            var cleanContent = (content ?? string.Empty).Trim();
            var cleanProvider = (provider ?? string.Empty).Trim();

            if (!Regex.IsMatch(normalizedCode, "^[A-Z0-9_-]{3,50}$"))
            {
                TempData["CouponError"] = "Mã chỉ được gồm chữ không dấu, số, dấu gạch ngang hoặc gạch dưới (3–50 ký tự).";
                return RedirectToAction(nameof(Index));
            }

            if (cleanContent.Length is < 2 or > 250 || cleanProvider.Length is < 2 or > 100)
            {
                TempData["CouponError"] = "Vui lòng nhập nội dung (2–250 ký tự) và nhà cung cấp (2–100 ký tự).";
                return RedirectToAction(nameof(Index));
            }

            if (normalizedType == "Percent")
            {
                if (discountValue <= 0 || discountValue > 99)
                {
                    TempData["CouponError"] = "Mức giảm theo phần trăm phải lớn hơn 0 và không quá 99%.";
                    return RedirectToAction(nameof(Index));
                }
            }
            else if (normalizedType == "Fixed")
            {
                if (discountValue <= 0 || discountValue > 1_000_000_000m || decimal.Truncate(discountValue) != discountValue)
                {
                    TempData["CouponError"] = "Mức giảm cố định phải là số nguyên VNĐ lớn hơn 0 và không quá 1 tỷ.";
                    return RedirectToAction(nameof(Index));
                }
            }
            else
            {
                TempData["CouponError"] = "Loại giảm giá không hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            if (expiresOn.HasValue && expiresOn.Value.Date < DateTime.UtcNow.AddHours(7).Date)
            {
                TempData["CouponError"] = "Ngày hết hạn phải là hôm nay hoặc một ngày trong tương lai.";
                return RedirectToAction(nameof(Index));
            }

            var alreadyExists = await _context.DiscountCoupons.AnyAsync(coupon => coupon.Code == normalizedCode);
            if (alreadyExists)
            {
                TempData["CouponError"] = $"Mã {normalizedCode} đã tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            DateTime? expiresAtUtc = null;
            if (expiresOn.HasValue)
            {
                var vietnamEndOfDay = DateTime.SpecifyKind(
                    expiresOn.Value.Date.AddDays(1).AddTicks(-1),
                    DateTimeKind.Unspecified);
                expiresAtUtc = TimeZoneInfo.ConvertTimeToUtc(vietnamEndOfDay, GetVietnamTimeZone());
            }

            _context.DiscountCoupons.Add(new DiscountCoupon
            {
                Code = normalizedCode,
                Content = cleanContent,
                Provider = cleanProvider,
                DiscountType = normalizedType,
                DiscountValue = discountValue,
                ExpiresAtUtc = expiresAtUtc,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            TempData["CouponSuccess"] = $"Đã tạo mã giảm giá {normalizedCode}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var coupon = await _context.DiscountCoupons.FirstOrDefaultAsync(item => item.Id == id);
            if (coupon == null)
            {
                TempData["CouponError"] = "Không tìm thấy mã giảm giá.";
                return RedirectToAction(nameof(Index));
            }

            coupon.IsActive = !coupon.IsActive;
            coupon.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["CouponSuccess"] = coupon.IsActive
                ? $"Đã bật mã {coupon.Code}."
                : $"Đã tắt mã {coupon.Code}.";
            return RedirectToAction(nameof(Index));
        }

        private static TimeZoneInfo GetVietnamTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
        }
    }
}
