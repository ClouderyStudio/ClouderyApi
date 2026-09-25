using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>后台人员鉴权：普通管理员或超级管理员均可通过（具体模块权限再用 <see cref="MhopPermAttribute"/> 限制）。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class MhopAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var accessor = context.HttpContext.RequestServices.GetRequiredService<MhopCurrentUserAccessor>();
        try
        {
            await accessor.RequireAdminAsync();
        }
        catch (MhopApiException ex)
        {
            context.Result = MhopJson.Error(ex.StatusCode, ex.Detail);
        }
    }
}

/// <summary>模块级权限：传入一个或多个权限码，拥有其中任意一个即放行（如撤回 AI 回复需 review 或 ai_logs）。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MhopPermAttribute(params string[] permissions) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var accessor = context.HttpContext.RequestServices.GetRequiredService<MhopCurrentUserAccessor>();
        try
        {
            await accessor.RequirePermAsync(permissions);
        }
        catch (MhopApiException ex)
        {
            context.Result = MhopJson.Error(ex.StatusCode, ex.Detail);
        }
    }
}

/// <summary>超级管理员专属：角色管理、权限分配等。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class MhopSuperAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var accessor = context.HttpContext.RequestServices.GetRequiredService<MhopCurrentUserAccessor>();
        try
        {
            await accessor.RequireSuperAsync();
        }
        catch (MhopApiException ex)
        {
            context.Result = MhopJson.Error(ex.StatusCode, ex.Detail);
        }
    }
}
