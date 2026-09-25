using Microsoft.AspNetCore.Mvc.Filters;

namespace ClouderyApi.Services.Mhop;

/// <summary>全局异常过滤器：把 MhopApiException 转成前端约定的 { detail } 错误体。</summary>
public sealed class MhopApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is MhopApiException ex)
        {
            context.Result = MhopJson.Error(ex.StatusCode, ex.Detail);
            context.ExceptionHandled = true;
        }
    }
}
