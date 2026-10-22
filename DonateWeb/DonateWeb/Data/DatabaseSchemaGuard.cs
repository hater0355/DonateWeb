using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Data;

/// <summary>
/// Đảm bảo schema đã được cập nhật bằng script migration được xem xét riêng.
/// Ứng dụng không tự thay đổi schema của database khi khởi động.
/// </summary>
public static class DatabaseSchemaGuard
{
    public static async Task EnsureMigrationsAppliedAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        if (!await context.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Không kết nối được SQL Server. Kiểm tra cấu hình kết nối và trạng thái database.");
        }

        string[] pendingMigrations;
        try
        {
            pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Không đọc được lịch sử EF Core migrations. Hãy chạy script schema đã được kiểm tra trước khi khởi động ứng dụng.",
                exception);
        }

        if (pendingMigrations.Length > 0)
        {
            var migrationList = string.Join(", ", pendingMigrations);
            throw new InvalidOperationException(
                $"Database còn migration chưa áp dụng: {migrationList}. Ứng dụng không tự cập nhật schema; hãy xem hướng dẫn trong scripts/database trước khi chạy script SQL.");
        }
    }
}
