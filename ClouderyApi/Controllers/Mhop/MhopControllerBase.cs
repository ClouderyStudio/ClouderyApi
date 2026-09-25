using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// MHOP 控制器基类：统一用 MhopJson 的蛇形命名 + UTC 时间序列化响应，
/// 保证与 Python 后端字段名一致，前端无需改动。
/// </summary>
public abstract class MhopControllerBase : ControllerBase
{
    protected IActionResult MhopOk(object? value) => new JsonResult(value, MhopJson.Options);

    protected IActionResult MhopStatus(int statusCode, object? value)
        => new JsonResult(value, MhopJson.Options) { StatusCode = statusCode };
}
