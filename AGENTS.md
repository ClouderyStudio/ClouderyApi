# AGENTS.md — 给 AI 编码代理的项目说明

本文件面向在本仓库工作的 AI 代理（Claude Code / Codex / DeepSeek Harness 等）。人类读者请看 [README.md](README.md)（功能 / 配置 / 接口）与 [DEPLOY.md](DEPLOY.md)（服务器与 CI 部署）。

## 1. 项目速览

- **ClouderyApi**：ASP.NET Core Web API（`net10.0`），单一程序集 + xUnit 测试项目，数据库为 **MySQL**（`MySql.EntityFrameworkCore`）。
- 服务四个站点族：云术 Cloudery（成员 / 内部试卷 / 云端测评结果 / 结果解读）、竹像素 Zhuxs（白名单 / 周目 / 申请）、MHOP 心理平台（论坛 / 漂流瓶 / 量表 / 后台）、以及 SurvivalCraft 与长链等工具接口。
- 认证并存：Casdoor Cookie 会话（Cloudery / Identity）、MHOP 自有 HS256 JWT、SurvivalCraft 静态 Token、基于 policy 的 `AdminOnly`。
- 代码组织：**模块化单体**，`ClouderyApi/Modules/<Ctx>/{Domain,Application,Api,Infrastructure}` + 共享内核 `ClouderyApi/Shared/`；四个 `DbContext` 分域（`ClouderyContext` / `ZhuxsContext` / `IdentityDbContext` / `MhopDbContext`）。详见 README 的「目录结构」。

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

# 运行时开关（执行完即退出，不启动 Web 主机）
dotnet ClouderyApi.dll --migrate [--seed] [--sweep-orphans [--delete-orphans]]
```

- `appsettings.json` 含密钥且**不入库**，本地从 `ClouderyApi/appsettings.example.json` 复制后填写；测试通过环境变量注入配置，不读该文件。
- **小改动只跑受影响范围**（`--filter`），批量收口时才跑全量；全量单跑约 2 分钟。

## 3. 硬约束（改动前必读）

对外契约必须**逐字不变**：

- 路由、CORS 白名单不变；路由表由 `ClouderyApi.Tests/SwaggerRouteSnapshotTests.cs` 守住。
- JSON：MHOP 用 snake_case + `{ "detail": "..." }` 错误体（`ClouderyApi/Shared/Json/MhopJson.cs`）；Cloudery / Zhuxs 返回裸对象 `{ success, message, ... }`。**中文文案不得改写**。
- 鉴权形状：只挂 `[Authorize]` 的端点未登录 → `401` + `WWW-Authenticate: Bearer` + 空响应体（**不是** 302 跳转）；只有 `[AdminOnly]`（端点授权元数据 ≤1 条）才由授权处理器写 `401 {"success":false,"message":"请先登录"}`；已登录非管理员一律 `403 {"success":false,"message":"无管理员权限，操作被拒绝"}`。
- 时间统一 UTC 带 `Z`；`inc_view` 必须是字符串（ASP.NET bool 绑定不接受 `"1"`，见 `ClouderyApi/Modules/Mhop/Api/MhopForumController.cs:45-48` 与同文件 `:127` 的 `IsTruthy`）。
- 数据库表名 / 列 / 索引不变，迁移只前滚、不写破坏性 `Down`；模型快照应与迁移一致（`dotnet ef migrations has-pending-model-changes --context <Ctx>` 应为 No changes）。
- 任何改动都要 `dotnet build -warnaserror` 通过，并按范围跑测试。

## 4. 编码约定

- **分层**：`Domain/` 放聚合、值对象与规则（不依赖 EF / HTTP）；`Application/` 放用例编排、`IClouderyDbContext` / `IZhuxsDbContext` 接口与 `Mapping/`；`Api/` 放控制器与 `Contracts/` 请求响应 DTO；`Infrastructure/` 放持久化与外部服务。
- **写接口绑定输入 DTO**，主键 / 时间戳由服务端生成，禁止直接绑定实体（over-posting）；请求校验交给 `[ApiController]` 自动 400，不要写不可达的 `ModelState` 分支。
- **配置走 Options 模式**（`ClouderyApi/Shared/Options/`、`MhopOptions`）；不要在类型加载时直接读 `appsettings.json`，也不要自建静态 `HttpClient`。
- **授权用特性 + policy**（`Shared/Authorization/`），不要用 service locator。
- **领域事件**：聚合以 `[NotMapped]` 事件列表实现 `IHasDomainEvents`，由 `MhopDbContext.SaveChangesAsync` 在保存成功后派发（`ClouderyApi/Shared/Domain/`）；新增副作用优先做成事件订阅者（`Modules/Mhop/Application/Events/`）。
- 不引入重量级依赖；保持 `Nullable` 开启、`.editorconfig` 与 `-warnaserror` 不新增警告。
- 迁移历史表：Zhuxs 用独立的 `__EFMigrationsHistory_Zhuxs`；Cloudery / Identity / Mhop 目前共用 `__EFMigrationsHistory`（调整前先确认，避免既有迁移被判定「未应用」而重跑）。

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

## 7. 文档地图

| 文档 | 用途 |
| ---- | ---- |
| [README.md](README.md) | 功能概览、配置项、各模块接口说明 |
| [DEPLOY.md](DEPLOY.md) | 1Panel 部署、GitHub Secrets、回滚与常见坑 |
| [docs/DDD-REFACTOR-PLAN.md](docs/DDD-REFACTOR-PLAN.md) | DDD 改造总记录：阶段里程碑、兼容性红线、已修缺陷 |
