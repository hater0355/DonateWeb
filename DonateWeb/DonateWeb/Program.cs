using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using DonateWeb.Services;
using DonateWeb.Areas.Admin.Services;
using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Security;

var builder = WebApplication.CreateBuilder(args);

// 1. Thêm cấu hình DbContext (Entity Framework Core với SQL Server)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// 2. Đăng ký các Services cho DI
builder.Services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAccountIdService, AccountIdService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IStreamerService, StreamerService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IWidgetService, WidgetService>();
builder.Services.AddScoped<IQrCodeService, QrCodeService>();

// Đăng ký Phân hệ Bảo mật & Kiểm duyệt (Content Moderation, Rate Limiting & Webhook Security)
builder.Services.AddSecurityAndModeration(builder.Configuration);

// 3. Cấu hình Authentication (Cookie & OAuth2 Bên thứ 3: Google, Facebook, YouTube)
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = "ExternalCookie";
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/AccessDenied";
    options.Cookie.Name = "DonateWeb.AuthCookie";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
})
.AddCookie("ExternalCookie", options =>
{
    options.Cookie.Name = "DonateWeb.ExternalAuthCookie";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
});

// 3.1. Cấu hình đăng nhập bằng Google
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
authBuilder.AddGoogle("Google", options =>
{
    options.SignInScheme = "ExternalCookie";
    options.ClientId = !string.IsNullOrWhiteSpace(googleClientId) ? googleClientId : "YOUR_GOOGLE_CLIENT_ID";
    options.ClientSecret = !string.IsNullOrWhiteSpace(googleClientSecret) ? googleClientSecret : "YOUR_GOOGLE_CLIENT_SECRET";
    options.CallbackPath = "/signin-google";
    options.SaveTokens = true;
    options.ClaimActions.MapJsonKey("urn:google:picture", "picture", "url");
    options.Events.OnRemoteFailure = context =>
    {
        var errorMsg = Uri.EscapeDataString(context.Failure?.Message ?? "Đã hủy hoặc xảy ra lỗi xác thực Google.");
        context.Response.Redirect($"/Auth/ExternalLoginCallback?remoteError={errorMsg}");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});

// 3.2. Cấu hình đăng nhập bằng Facebook
var fbAppId = builder.Configuration["Authentication:Facebook:AppId"];
var fbAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];
authBuilder.AddFacebook("Facebook", options =>
{
    options.SignInScheme = "ExternalCookie";
    options.AppId = !string.IsNullOrWhiteSpace(fbAppId) ? fbAppId : "YOUR_FACEBOOK_APP_ID";
    options.AppSecret = !string.IsNullOrWhiteSpace(fbAppSecret) ? fbAppSecret : "YOUR_FACEBOOK_APP_SECRET";
    options.CallbackPath = "/signin-facebook";
    options.SaveTokens = true;
    options.Fields.Add("name");
    options.Fields.Add("email");
    options.Fields.Add("picture");
    options.Events.OnRemoteFailure = context =>
    {
        var errorMsg = Uri.EscapeDataString(context.Failure?.Message ?? "Đã hủy hoặc xảy ra lỗi xác thực Facebook.");
        context.Response.Redirect($"/Auth/ExternalLoginCallback?remoteError={errorMsg}");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});

// 3.3. Cấu hình đăng nhập bằng YouTube (Sử dụng Google OAuth 2.0 + YouTube Data API Scope)
var ytClientId = builder.Configuration["Authentication:YouTube:ClientId"];
var ytClientSecret = builder.Configuration["Authentication:YouTube:ClientSecret"];
authBuilder.AddGoogle("YouTube", "YouTube", options =>
{
    options.SignInScheme = "ExternalCookie";
    options.ClientId = !string.IsNullOrWhiteSpace(ytClientId) ? ytClientId : (!string.IsNullOrWhiteSpace(googleClientId) ? googleClientId : "YOUR_YOUTUBE_CLIENT_ID");
    options.ClientSecret = !string.IsNullOrWhiteSpace(ytClientSecret) ? ytClientSecret : (!string.IsNullOrWhiteSpace(googleClientSecret) ? googleClientSecret : "YOUR_YOUTUBE_CLIENT_SECRET");
    options.CallbackPath = "/signin-youtube";
    options.SaveTokens = true;
    options.Scope.Add("https://www.googleapis.com/auth/youtube.readonly");
    options.ClaimActions.MapJsonKey("urn:google:picture", "picture", "url");
    options.Events.OnRemoteFailure = context =>
    {
        var errorMsg = Uri.EscapeDataString(context.Failure?.Message ?? "Đã hủy hoặc xảy ra lỗi xác thực YouTube.");
        context.Response.Redirect($"/Auth/ExternalLoginCallback?remoteError={errorMsg}");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});

// 4. Cấu hình Phân quyền (RBAC - Role-based Access Control)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy => policy.RequireRole(UserRoles.Admin));
    options.AddPolicy("RequireStreamerRole", policy => policy.RequireRole(UserRoles.Streamer));
    options.AddPolicy("RequireViewerRole", policy => policy.RequireRole(UserRoles.Viewer));
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

// 5. Tự động khởi tạo Database và Seed dữ liệu mẫu (Roles, Admin, Streamer tenstreamer, Viewer)
using (var scope = app.Services.CreateScope())
{
    await DbInitializer.InitializeAsync(app.Services);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Bật Authentication trước Authorization
app.UseAuthentication();
app.UseAuthorization();

// Ánh xạ các API Controllers (bao gồm PaymentWebhookController và WidgetsApiController)
app.MapControllers();

// 6. Cấu hình Route cho Area Admin
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Admin}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "admin_shortcut",
    pattern: "Admin/{action=Index}/{id?}",
    defaults: new { area = "Admin", controller = "Admin" });

// 7. Cấu hình Route cho Slug Streamer (ví dụ: domain.com/tenstreamer hoặc domain.com/domixi)
// Sử dụng regex để tránh xung đột với các route controller hoặc tài nguyên tĩnh
app.MapControllerRoute(
    name: "streamer_slug",
    pattern: "{slug:regex(^(?!api|auth|admin|widgets|home|profile|streamer|wallet|coupons|effects|orders|transactions|guides|images|css|js|lib|favicon).*$)}",
    defaults: new { controller = "Streamer", action = "Donate" });

// Route mặc định
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();