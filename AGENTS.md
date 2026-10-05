# AGENTS.md — 给 AI 编码代理的项目说明

本文件面向在本仓库工作的 AI 代理（Claude Code / Codex / DeepSeek Harness 等）。人类读者请看 [README.md](README.md)（功能 / 配置 / 接口）与 [DEPLOY.md](DEPLOY.md)（服务器与 CI 部署）。

## 1. 项目速览

- **ClouderyApi**：ASP.NET Core Web API（`net10.0`），单一程序集 + xUnit 测试项目，数据库为 **MySQL**（`MySql.EntityFrameworkCore`）。
- 服务四个站点族：云术 Cloudery（成员 / 内部试卷 / 云端测评结果 / 结果解读）、竹像素 Zhuxs（白名单 / 周目 / 申请）、MHOP 心理平台（论坛 / 漂流瓶 / 量表 / 后台）、以及 SurvivalCraft 与长链等工具接口。
- 认证并存：Casdoor Cookie 会话（Cloudery / Identity）、MHOP 自有 HS256 JWT、SurvivalCraft 静态 Token、基于 policy 的 `AdminOnly`。
- 代码组织：**模块化单体**，`ClouderyApi/Modules/<Ctx>/{Domain,Application,Api,Infrastructure}` + 共享内核 `ClouderyApi/Shared/`；**五个** `DbContext` 分域（`ClouderyContext` / `ZhuxsContext` / `IdentityDbContext` / `MhopDbContext` / `ScforgeDbContext`）。详见 README 的「目录结构」。

## 2. 常用命令

```bash
# 构建（CI 门禁：0 警告 0 错误）
dotnet build ClouderyApi.sln --configuration Release -warnaserror

# 测试：集成测试每个测试类自建一次性 MySQL 库，需要一个可连的 MySQL
export CLOUDERY_TEST_MYSQL="server=127.0.0.1;port=3306;user=root;password=root"
dotnet test ClouderyApi.sln --configuration Release
dotnet test ClouderyApi.Tests --filter "FullyQualifiedName~ExamPapersContractTests"   # 只跑受影响范围

# 迁移（必须带 --context 与 --output-dir）
dotnet ef migrations add <Name> --context ClouderyContext  --output-dir Migrations/Cloudery
dotnet ef migrations add <Name> --context ZhuxsContext     --output-dir Migrations/Zhuxs
dotnet ef migrations add <Name> --context IdentityDbContext --output-dir Migrations/Identity
dotnet ef migrations add <Name> --context MhopDbContext    --output-dir Migrations/Mhop
dotnet ef migrations add <Name> --context ScforgeDbContext --output-dir Migrations/Scforge

# 运行时开关（执行完即退出，不启动 Web 主机）
dotnet ClouderyApi.dll --migrate [--seed] [--sweep-orphans [--delete-orphans]]
```

- `appsettings.json` 含密钥且**不入库**，本地从 `ClouderyApi/appsettings.example.json` 复制后填写；测试通过环境变量注入配置，不读该文件。
- **小改动只跑受影响范围**（`--filter`），批量收口时才跑全量；全量单跑约 2 分钟。

### 2.1 必须先在本地跑通，不要靠 CI 兜底

**动手前先自备一个本地 MySQL 8**（CI 里的服务容器本地没有；不装就等于用「编译过 + 单测过」冒充验证过）：

```bash
# Docker（推荐，一次性干净实例）
docker run -d --name cloudery-mysql -p 3306:3306 \
  -e MYSQL_ROOT_PASSWORD=root -e MYSQL_DATABASE=api mysql:8.0
```

连不上时先自查容器状态与端口占用；已有实例复用即可，**不要**每次重建。

- **集成测试必须有真实 MySQL 才算数**：每个测试类自建 / 自删一次性库，没有库时它们会以
  `MySqlException: Unable to connect to any of the specified MySQL hosts` 批量失败。
  **看到成片的这类失败一律先确认是不是环境没起，而不是当成代码回归。**
- **改了迁移、DI 注册、`--migrate` 启动路径或中间件，必须在本地实跑一次**
  `dotnet ClouderyApi.dll --migrate`，看日志里出现 `Applying migration '...'` 与目标 `CREATE TABLE`，
  再跑受影响的测试。历史事故见第 6 节「迁移只迁了一半」。

## 3. 硬约束（改动前必读）

对外契约必须**逐字不变**：

- 路由、CORS 白名单不变；路由表由 `ClouderyApi.Tests/SwaggerRouteSnapshotTests.cs` 守住。
- JSON：成功体维持现状（MHOP snake_case + 扁平对象；Cloudery / Zhuxs 裸对象 `{ success, message, ... }`）；**所有错误体统一为 `{ "detail": "..." }`**（可选 `errors` / `retryAfterSeconds`，由 `ClouderyApi/Shared/Json/ApiError.cs` 与 `ApiErrorBodyMiddleware` 产出，见 `docs/API-ERROR-SHAPE.md`）。**中文文案不得改写**。
- 鉴权形状：只挂 `[Authorize]` 的端点未登录 → `401` + `WWW-Authenticate: Bearer`，响应体由 `ApiErrorBodyMiddleware` 补成 `{"detail":"请先登录"}`（**不是** 302 跳转）；`[AdminOnly]`（端点授权元数据 ≤1 条）由授权处理器直接写 `401 {"detail":"请先登录"}`；已登录非管理员一律 `403 {"detail":"无管理员权限，操作被拒绝"}`。
- 时间统一 UTC 带 `Z`；`inc_view` 必须是字符串（ASP.NET bool 绑定不接受 `"1"`，见 `ClouderyApi/Modules/Mhop/Api/MhopForumController.cs:45-48` 与同文件 `:127` 的 `IsTruthy`）。
- 数据库表名 / 列 / 索引不变，迁移只前滚、不写破坏性 `Down`；模型快照应与迁移一致（`dotnet ef migrations has-pending-model-changes --context <Ctx>` 应为 No changes）。
- 任何改动都要 `dotnet build -warnaserror` 通过，并按范围跑测试。**涉及迁移 / DI / 启动路径 / 中间件时，「build 与单测通过」不算验证过**——必须按 2.1 在本地实跑（本地 MySQL 8 + `--migrate`）。

## 4. 编码约定

- **分层**：`Domain/` 放聚合、值对象与规则（不依赖 EF / HTTP）；`Application/` 放用例编排、`IClouderyDbContext` / `IZhuxsDbContext` 接口与 `Mapping/`；`Api/` 放控制器与 `Contracts/` 请求响应 DTO；`Infrastructure/` 放持久化与外部服务。
- **写接口绑定输入 DTO**，主键 / 时间戳由服务端生成，禁止直接绑定实体（over-posting）；请求校验交给 `[ApiController]` 自动 400，不要写不可达的 `ModelState` 分支。
- **配置走 Options 模式**（`ClouderyApi/Shared/Options/`、`MhopOptions`）；不要在类型加载时直接读 `appsettings.json`，也不要自建静态 `HttpClient`。
- **授权用特性 + policy**（`Shared/Authorization/`），不要用 service locator。
- **领域事件**：聚合以 `[NotMapped]` 事件列表实现 `IHasDomainEvents`，由 `MhopDbContext.SaveChangesAsync` 在保存成功后派发（`ClouderyApi/Shared/Domain/`）；新增副作用优先做成事件订阅者（`Modules/Mhop/Application/Events/`）。
- 不引入重量级依赖；保持 `Nullable` 开启、`.editorconfig` 与 `-warnaserror` 不新增警告。
- 迁移历史表：Zhuxs 用独立的 `__EFMigrationsHistory_Zhuxs`；Cloudery / Identity / Mhop 目前共用 `__EFMigrationsHistory`（调整前先确认，避免既有迁移被判定「未应用」而重跑）。三个共用表意味着**迁移 ID 必须全局唯一**，撞车会被 EF 静默跳过。

## 4.1 安全红线（改动前必读）

- **禁止在代码里写死任何口令**。种子超管口令来自 `Mhop:SeedAdminPassword`，留空则生成随机强口令并只写一次日志。历史事故：`admin123` / `1234567` 两个超管口令曾作为字面量进入源码；`MhopSeedAdminSecurityTests` 会让这种回归直接失败。
- **公开资料接口不得下发登录凭据**。`GET /mhop/auth/users/{id}` 匿名可访问，`MhopAuthMapper.ToUserOut` 必须传 `maskEmail: true` + `maskPhone: true` + `exposePermissions: false`；本人 / 后台接口用默认值。邮箱是邮箱验证码登录的唯一凭据，泄漏即可按 id 遍历全站 PII。
- **客户端 IP 一律走 `ClientIp.Resolve(HttpContext)`**，不要直接读 `RemoteIpAddress`。生产在 Nginx 之后，直读拿到的是代理 IP，限流会退化成「全站一个桶」。可信代理在 `TrustedProxies` 节显式声明——**绝不要信任任意来源**，否则伪造 `X-Forwarded-For` 就能绕过限流。
- **`Mhop:Smtp:AllowInvalidCertificate` 生产保持 false**。打开后 TLS 不校验证书，登录验证码会被中间人截获。
- **触发 LLM 的端点必须挂 `[IpRateLimit]`**：目前是 `/mhop/assessments`、`/mhop/forum/posts`、`/mhop/forum/posts/{id}/replies`、`/mhop/bottles`、`/mhop/bottles/{id}/messages`。429 响应体由 `ApiError` 统一写成 `{ "detail": ..., "retryAfterSeconds": ... }`（MHOP 前端读 `detail`）。新增同类端点时一并补上。

## 5. 提交与协作约定

- 提交信息用中文 conventional 前缀：`refactor(scope): ...`、`fix(scope): ...`、`docs: ...`、`test(scope): ...`。
- 一个可验证的步骤一个提交，提交前 build（+ 按范围测试）。
- **默认只提交到本地，不要 `git push`**：推送 `master` 会触发自动部署（见 `DEPLOY.md`），必须由用户明确同意。
- 不要提交 `appsettings.json`、任何密钥、`uploads/`、`bin/`、`obj/`。
- 改动前先读文件；不做请求范围之外的重构。

## 6. 常见坑

- 工作树文件是 **CRLF**：脚本里做多行字符串替换要先归一化 `\r\n` → `\n` 再还原，否则报「找不到匹配」。
- MySQL 默认 `max_connections=151`：并发跑多个 `dotnet test` 会 `Too many connections`（CI 用 `SET GLOBAL max_connections=500` 解决），本地不要并行跑测试。
- 集成测试库按测试类创建 / 删除，不要假定某个库已存在。
- 生产环境 `Mhop:AutoMigrate` / `Mhop:Seed` 关闭，迁移与种子走 CLI `--migrate` / `--seed`（`.github/workflows/deploy.yml` 已在重启容器前执行）。
- `--sweep-orphans` 默认只预览孤儿图片，实际删除需显式 `--delete-orphans`。
- **迁移只迁了一半**（2026-10-04 事故）：`--migrate` 经 `DatabaseMaintenanceService` 只驱动
  `MhopDbContext`，**`ScforgeDbContext` 从未纳入**。Scforge 的表是当初手工 `dotnet ef` 建上的，
  此后新增迁移从未执行 —— 表现是**部署全绿但新表不存在**，带 Bearer 的请求一律 500，
  且因中间件跑在管道早期且覆盖所有端点，**公开接口也被匿名请求打挂**。
  已修（新增 `ScforgeMaintenanceService` 并在 `--migrate` 一并前滚）。
  ⚠️ **新增 DbContext 时必须同步接进 `--migrate`**；新增中间件里碰数据库的服务时，
  **查库异常必须降级为匿名（fail-closed）**，否则一个伪造的 Authorization 头就能放大成全站故障。
- **新建服务类必须同步 `AddScoped`**：2026-10-04 部署失败于
  `No service for type 'ScforgeMaintenanceService' has been registered.`
  —— 编译 0 错、单测全绿，只有真部署才炸。
  所以**新服务 / 改 `--migrate` / 改 DI 后必须在本地实跑一次**（见 2.1）。

## 7. 文档地图

| 文档 | 用途 |
| ---- | ---- |
| [README.md](README.md) | 功能概览、配置项、各模块接口说明 |
| [DEPLOY.md](DEPLOY.md) | 1Panel 部署、GitHub Secrets、回滚与常见坑 |
| [docs/DDD-REFACTOR-PLAN.md](docs/DDD-REFACTOR-PLAN.md) | DDD 改造总记录：阶段里程碑、兼容性红线、已修缺陷 |
