using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using DonateWeb.Security.Configuration;
using DonateWeb.ViewModels.Streamer;

namespace DonateWeb.Security.RateLimiting
{
    /// <summary>
    /// ACTION FILTER BẢO VỆ FORM TẠO ĐƠN DONATE KHỎI SPAM BOT & GỬI ĐƠN ẢO LIÊN TỤC
    /// Thư mục riêng: Security/RateLimiting/
    /// Cách dùng: Đặt [DonateRateLimit] phía trên action [HttpPost] Donate trong StreamerController.
    /// Hoạt động:
    /// - Đo lường số request tạo đơn donate từ địa chỉ IP của client trong 1 phút.
    /// - Nếu vượt quá ngưỡng (mặc định 5 request/phút cấu hình trong appsettings.json):
    ///   + Trả về HTTP Status Code 429 Too Many Requests
    ///   + Thêm Header Retry-After
    ///   + Hiển thị thông báo cảnh báo chống spam bot rõ ràng cho người dùng
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class DonateRateLimitAttribute : ActionFilterAttribute
    {
        private readonly int? _customMaxRequests;
        private readonly int? _customWindowSeconds;

        /// <summary>
        /// Khởi tạo filter với giới hạn tùy chọn, hoặc để trống để sử dụng giá trị từ appsettings.json
        /// </summary>
        public DonateRateLimitAttribute(int maxRequests = 0, int windowSeconds = 0)
        {
            if (maxRequests > 0) _customMaxRequests = maxRequests;
            if (windowSeconds > 0) _customWindowSeconds = windowSeconds;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var services = httpContext.RequestServices;

            var rateLimiter = services.GetRequiredService<IIpRateLimiterService>();
            var securitySettings = services.GetRequiredService<IOptions<SecuritySettings>>().Value;

            var maxRequests = _customMaxRequests ?? securitySettings.RateLimiting.DonateRequestsPerMinute;
            var windowSeconds = _customWindowSeconds ?? securitySettings.RateLimiting.WindowSeconds;
            var window = TimeSpan.FromSeconds(windowSeconds);

            // Phân giải IP của Client
            var clientIp = rateLimiter.ResolveClientIp(httpContext);

            // Kiểm tra giới hạn tần suất
            var isAllowed = rateLimiter.CheckLimit(
                clientIp,
                actionKey: "donate_create",
                maxRequests,
                window,
                out var remainingRequests,
                out var retryAfter);

            // Gắn Header RateLimit tiêu chuẩn RFC
            httpContext.Response.Headers["X-RateLimit-Limit"] = maxRequests.ToString();
            httpContext.Response.Headers["X-RateLimit-Remaining"] = remainingRequests.ToString();

            if (!isAllowed)
            {
                var retrySeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                httpContext.Response.Headers["Retry-After"] = retrySeconds.ToString();
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var isApiOrAjax = httpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                    || httpContext.Request.Headers.Accept.ToString().Contains("application/json")
                    || httpContext.Request.Path.StartsWithSegments("/api");

                var warningMessage = $"⚠️ CẢNH BÁO CHỐNG SPAM BOT: Địa chỉ IP ({clientIp}) đã gửi quá nhiều yêu cầu Donate trong 1 phút (tối đa {maxRequests} lần). Vui lòng đợi {retrySeconds} giây trước khi gửi tiếp.";

                if (isApiOrAjax)
                {
                    context.Result = new JsonResult(new
                    {
                        success = false,
                        error = "TOO_MANY_REQUESTS",
                        message = warningMessage,
                        retryAfterSeconds = retrySeconds
                    })
                    {
                        StatusCode = StatusCodes.Status429TooManyRequests
                    };
                    return;
                }

                // Xử lý giao diện MVC Form Donate thông thường
                if (context.Controller is Controller controller)
                {
                    controller.ModelState.AddModelError(string.Empty, warningMessage);

                    // Tìm model trong arguments để nạp lại vào View
                    var model = context.ActionArguments.Values.OfType<StreamerDonateViewModel>().FirstOrDefault();
                    if (model != null)
                    {
                        model.ErrorMessage = warningMessage;
                        context.Result = controller.View(model);
                        return;
                    }

                    context.Result = controller.View();
                    return;
                }

                context.Result = new ContentResult
                {
                    StatusCode = StatusCodes.Status429TooManyRequests,
                    Content = warningMessage,
                    ContentType = "text/plain; charset=utf-8"
                };
                return;
            }

            await next();
        }
    }
}
