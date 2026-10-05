using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Modules.Scforge.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using ClouderyApi.Shared.Json;

namespace ClouderyApi.Modules.Scforge.Api;

/// <summary>
/// SCForge 控制器基类。
///
/// 负责两件在模块内保持一致的事：
///   1. **错误形状**：失败一律是统一错误体 <c>{ "detail": "…" }</c>（<see cref="ApiError"/> 产出）；
///   2. **业务异常翻译**：领域抛出的 <see cref="ScforgeRuleException"/> /
///      <see cref="ScforgeApiException"/> 在这里变成 400 / 401 / 403 / 404 / 409，
///      不依赖全局 MhopApiExceptionFilter（它只认 MHOP 的异常类型），错误体仍是上面的统一形状。
///
/// 鉴权同样是显式的：写端点不挂 [Authorize]，而是先构造 actor 与 admin 上下文，
/// 由应用层按「是不是作者 / 有没有某个权限」判定，未登录返回中文 401 体。
/// </summary>
[ApiController]
public abstract class ScforgeControllerBase : ControllerBase
{
    /// <summary>把当前请求的身份转成应用层值对象。</summary>
    protected static ScforgeActor ToActor(ScforgeCurrentUser current) =>
        new(current.UserId, current.DisplayName, current.Avatar);

    /// <summary>统一把领域异常翻译成模块约定的错误体。</summary>
    protected async Task<IActionResult> GuardAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ScforgeRuleException ex)
        {
            return ApiError.Result(400, ex.Message);
        }
        catch (ScforgeApiException ex)
        {
            return ApiError.Result(ex.StatusCode, ex.Message);
        }
    }

    /// <summary>只执行副作用、不需要回传实体的写操作。</summary>
    protected Task<IActionResult> GuardAsync(Func<Task> action) =>
        GuardAsync(async () =>
        {
            await action();
            return Ok(new { success = true });
        });
}
