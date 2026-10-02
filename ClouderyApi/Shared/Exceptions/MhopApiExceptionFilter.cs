using ClouderyApi.Shared.Json;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClouderyApi.Shared.Exceptions;

/// <summary>
/// 全局异常过滤器：把 MhopApiException 转成前端约定的 { detail } 错误体。
/// 领域层抛出的 DomainRuleException 没有状态码，一律按 400 返回其用户可读原因。
/// </summary>
public sealed class MhopApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case MhopApiException ex:
                context.Result = MhopJson.Error(ex.StatusCode, ex.Detail);
                context.ExceptionHandled = true;
                break;
            case DomainRuleException ex:
                context.Result = MhopJson.Error(400, ex.Message);
                context.ExceptionHandled = true;
                break;
        }
    }
}
