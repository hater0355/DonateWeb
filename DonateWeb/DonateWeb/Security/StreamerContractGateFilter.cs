using System.Security.Claims;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Security;

/// <summary>Chặn tài khoản streamer đã duyệt nhưng chưa chấp nhận phiên bản hợp đồng bắt buộc.</summary>
public sealed class StreamerContractGateFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true || principal.IsInRole(UserRoles.Admin) ||
            context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null)
        {
            await next();
            return;
        }

        var action = context.ActionDescriptor as ControllerActionDescriptor;
        var controller = action?.ControllerName;
        var actionName = action?.ActionName;
        if ((controller == "Streamer" && actionName is "Contract" or "ContractAccept") ||
            (controller == "Streamer" && actionName == "CancelStreamer") ||
            (controller == "Auth" && actionName is "Logout" or "Login" or "AccessDenied" or "ExternalLoginCallback"))
        {
            await next();
            return;
        }

        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await next();
            return;
        }

        var pending = await db.StreamerProfiles.AsNoTracking()
            .Where(profile => profile.UserId == userId &&
                              profile.ApprovalStatus == StreamerApprovalStatus.Approved &&
                              profile.RequiredContractVersion != null)
            .Select(profile => new { profile.Id, profile.RequiredContractVersion })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (pending != null && !await db.StreamerContractAcceptances.AsNoTracking().AnyAsync(
                acceptance => acceptance.StreamerProfileId == pending.Id &&
                              acceptance.ContractVersion == pending.RequiredContractVersion,
                context.HttpContext.RequestAborted))
        {
            context.Result = new RedirectToActionResult("Contract", "Streamer", null);
            return;
        }

        await next();
    }
}
