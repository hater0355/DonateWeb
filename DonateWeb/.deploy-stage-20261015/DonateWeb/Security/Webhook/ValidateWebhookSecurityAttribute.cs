using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DonateWeb.Security.Webhook
{
    /// <summary>
    /// ACTION FILTER BẢO VỆ CÁC ENDPOINT WEBHOOK
    /// Thư mục riêng: Security/Webhook/
    /// Tự động chặn các request nếu:
    /// 1. IP không nằm trong Whitelist (trả về 403 Forbidden)
    /// 2. Secret Key hoặc Chữ ký HMAC không khớp (trả về 401 Unauthorized)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class ValidateWebhookSecurityAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var webhookSecurity = httpContext.RequestServices.GetRequiredService<IWebhookSecurityService>();

            // Đọc nội dung Body thô để phục vụ xác thực chữ ký HMAC nếu cần
            httpContext.Request.EnableBuffering();
            string rawBody = string.Empty;
            using (var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync();
                httpContext.Request.Body.Position = 0; // Reset vị trí đọc để model binding xử lý tiếp
            }

            var (isValid, statusCode, errorMessage) = await webhookSecurity.ValidateRequestAsync(httpContext, rawBody);

            if (!isValid)
            {
                context.Result = new JsonResult(new
                {
                    success = false,
                    error = statusCode == StatusCodes.Status403Forbidden ? "IP_FORBIDDEN" : "UNAUTHORIZED",
                    message = errorMessage
                })
                {
                    StatusCode = statusCode
                };
                return;
            }

            await next();
        }
    }
}
