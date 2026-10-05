# API 统一错误响应体（{ detail }）

> 状态：已落地（2026-10-05）。相关提交见仓库历史的 `refactor(api)` / `test(api)` 系列。
> 实现：`ClouderyApi/Shared/Json/ApiError.cs`、`ClouderyApi/Shared/Json/ApiErrorBodyMiddleware.cs`、
> `ClouderyApi/Program.cs`（`ApiBehaviorOptions.InvalidModelStateResponseFactory` + 中间件注册）。

## 1. 结论

全站所有状态码 **≥ 400** 的响应，只要没有业务体，统一写成：

```json
{ "detail": "中文文案" }
```

两个可选扩展字段：

| 字段 | 出现场合 | 说明 |
| ---- | ---- | ---- |
| `errors` | 仅 `[ApiController]` 自动校验 400 | 字段名 → 错误消息数组，如 `{"Name":["The Name field is required."]}` |
| `retryAfterSeconds` | 仅 429 | 与 `Retry-After` 响应头同源，两者必须一致 |

**成功体一律不变**：MHOP 仍是 snake_case 扁平对象（如 `{access_token, user, ...}`），
Cloudery / Zhuxs 仍是裸对象 `{ success, message, ... }`，`/identity/auth/status` 匿名仍是
`{ isAuthenticated:false, message:"未登录" }`。`204 No Content` 仍是空体。

## 2. 四个产生路径

| 路径 | 位置 | 用法 |
| ---- | ---- | ---- |
| 控制器 / 授权处理器 | `ApiError.Result(status, detail, errors?, retryAfterSeconds?)` | 返回 `JsonResult`（可被 `ActionResult<T>` 直接 return） |
| 中间件 / 过滤器直接写体 | `await ApiError.WriteAsync(context, status, detail)` | 会清掉上游写入的 `Content-Length: 0` |
| 模型校验 400 | `Program.cs` 的 `InvalidModelStateResponseFactory` | `ApiError.Result(400, ApiError.ValidationDetail, errors)` |
| 框架空体兜底 | `ApiErrorBodyMiddleware`（`Program.cs:387`，紧跟 `UseForwardedHeaders`，管道最前） | 见下 |

`ApiErrorBodyMiddleware` 的两条规则：

1. **未处理异常**（且响应未开始）→ `logger.LogError(ex, ...)`（含堆栈）+ `500 {"detail":"服务器内部错误"}`。
   **全环境一致，包括 Development**：项目没有注册开发者异常页 / `UseExceptionHandler`，
   rethrow 只会得到空体 500，反而不一致；排查看日志。
2. **状态码 ≥ 400 且 `!Response.HasStarted` 且 `ContentType is null`** → 按状态码补
   `ApiError.DefaultDetail(status)`。已有响应体（控制器、授权处理器、限流）不会被覆盖。

## 3. 状态码兜底文案（`ApiError.DefaultDetail`）

| 状态码 | detail |
| ---- | ---- |
| 400 | 请求参数有误（校验失败走 `参数校验失败`） |
| 401 | 请先登录 |
| 403 | 没有访问权限 |
| 404 | 资源不存在 |
| 405 | 请求方法不被允许 |
| 406 | 无法生成请求的响应格式 |
| 408 | 请求超时，请稍后再试 |
| 409 | 请求与当前资源状态冲突 |
| 413 | 请求内容过大 |
| 415 | 不支持的请求内容类型 |
| 429 | 请求过于频繁，请稍后再试 |
| ≥ 500 | 服务器内部错误 |
| 其他 | 请求失败 |

控制器自己写的具体文案优先，例如：`缺少量表标识（testId）`、`未找到该试卷`、`试卷ID已存在`、
`记录不存在`、`记录冲突`、`只有作者可以发布新版本`、`驳回时必须填写理由`、`插件请上传 .dll 文件`、
`非法的服务器路径`、`后端请求失败: ...`、`无效的跳转链接`、`跨站请求被拒绝`、
`分析请求过于频繁，请稍后再试`、`无管理员权限，操作被拒绝`。

## 4. 例外（保持原样）

- `SurvivalCraft` 的上游透传：`GET/POST /server/{...}` 把上游状态码与上游响应体原样返回，不套 `{detail}`。
- Swagger UI / 静态文件 / `uploads/` 的成功响应。
- 健康检查 `/mhop/health`。

## 5. 前端消费

| 前端 | 位置 | 处理 |
| ---- | ---- | ---- |
| mhop-frontend | `src/api/index.js:20` | 读 `error.response?.data?.detail` —— 形状不变，**零改动** |
| psychology | `login.vue` / `test/[id].vue` / `account.vue` / `NavBar.vue` / `useCloudSync.ts` / `admin.vue` | 原来读 `e?.data?.message`，已补 `detail` 兜底 |
| scforge-frontend / qisoul | 读错误体文案处 | 同上补 `detail` 兜底 |

## 6. 验证与守卫

- `ClouderyApi.Tests/ApiErrorShapeContractTests.cs`：未知路由 404、`GET /identity/auth/callback` 405、
  `text/plain` POST 415、框架鉴权 challenge 401 都必须只有 `detail` 一个字段。
- 各模块契约测试（Cloudery / Identity / Exam / ExamPapers / Zhuxs / Scforge / Server / ResultAnalysis /
  IpRateLimit / Cookie 鉴权）的 400/401/403/404/409/429 断言全部按 `{ detail }` 校验。
- 回归门禁：`dotnet build ClouderyApi.sln --configuration Release -warnaserror` + 全量 `dotnet test`。

## 7. 决策记录

- **为什么是 `detail` 而不是 `message`**：MHOP 侧本就用 `{ detail }`，mhop-frontend 唯一的错误出口
  （`src/api/index.js:20`）只读 `detail`，全站 40+ 处既有契约测试也断言 `detail`。
  选 `detail` 改动面最小（MHOP 零回归），代价是 psychology / scforge 侧要补一层兜底。
- **为什么加兜底中间件而不是逐处改框架行为**：框架产生的空体 401/404/405/415 无法在控制器层统一；
  中间件放在管道最前，一次覆盖路由、鉴权、MVC 与限流。
- **校验失败为什么不再用 `ValidationProblemDetails`**：`SuppressMapClientErrors = true` +
  自定义 `InvalidModelStateResponseFactory`，`problem+json` 与 `{success:false,message}` 两种旧形状
  同时消失，前端只需认一种。
- **路由表与成功体不受影响**：`SwaggerRouteSnapshotTests` 继续守住路由；
  本改动不触碰任何成功响应。
