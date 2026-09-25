using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>管理员鉴权：等价于 Python 后端 admin 路由的 dependencies=[Depends(get_admin)]。</summary>
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
