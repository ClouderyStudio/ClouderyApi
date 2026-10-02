# Stage 5 横切与工程化：施工图

本文是 [docs/DDD-REFACTOR-PLAN.md](docs/DDD-REFACTOR-PLAN.md) Stage 5（第 313–320 行）与附录 A 的施工图。**本文只描述改法，不改代码。** 所有行号以当前工作树 HEAD `8aec30c`（Stage 3 目录重组后）核对；涉及 `Modules/{Cloudery,Zhuxs}/Api/` 与 `Modules/Identity/Api/AuthController.cs` 的位置标注「Stage 2 M4 后将变动」。

改造成两类：**零契约影响的机械改造**（直接做）与**会改变对外行为的改造（需单独批准）**，后者集中列在第三章。

## 一、现状证据表

| # | 事项 | 现状证据（file:line） |
|---|---|---|
| 1 | ClouderyApiContext 6 个 DbSet | `ClouderyApi/Data/ClouderyApiContext.cs:12-17`；转换器 :22-51；ExamResult 唯一索引 :56-58、普通索引 :61-62 |
| 1 | 引用点（6 应用服务 + DI） | `ClouderyApi/Modules/Zhuxs/Application/WhitelistsAppService.cs:13`、`TermsAppService.cs:13`、`ApplicationsAppService.cs:13`、`ClouderyApi/Modules/Cloudery/Application/MembersAppService.cs:13`、`ExamPaperAppService.cs:31`、`ExamResultService.cs:16`、`Program.cs:32` |
| 1 | 设计时工厂 | 只有 `Modules/Mhop/Infrastructure/Persistence/MhopDbContextFactory.cs`、`Modules/Identity/Infrastructure/Persistence/IdentityDbContextFactory.cs`（各 31 行）；ClouderyApiContext **无**工厂 |
| 2 | 配置直读 | `Shared/Filters/AdminOnlyAttribute.cs:26-30`、`Modules/Identity/Api/AuthController.cs:34-43`、`Modules/SurvivalCraft/Api/ServerController.cs:28-36`、`Modules/Mhop/Application/CasdoorAppService.cs:31/33/35/37/39-41/50/105/107/108/127`、`Program.cs:43/46/73/91/96/101/125/179/192/274` |
| 3 | AdminOnly 过滤器 | `Shared/Filters/AdminOnlyAttribute.cs:14`（IAuthorizationFilter），401 :19-23，service-locator :26-30，403 :32-39 |
| 3 | [AdminOnly] 命中 14 处 | WhitelistsController.cs:15（类级）；TermsController.cs:31/41/61；ApplicationsController.cs:31/41/61；MembersController.cs:31/41/61；ExamPapersController.cs:32/51/67/79 |
| 4 | 启动期 Migrate/Seed | `Program.cs:179-190`（`Mhop:AutoMigrate` 默认 IsDevelopment）、`:192-206`（`Mhop:Seed` 默认 true）；全仓库唯一 Migrate 调用 :184 |
| 4 | Seed 幂等实现 | `Modules/Mhop/Infrastructure/MhopSeeder.cs:22-89`：admin 存在性 :24、admin→SuperAdmin :40-42、root 存在性 :49、引导帖 :62 |
| 5 | 无独立历史表 | 全仓库 `MigrationsHistoryTable` 0 命中；三上下文同连接 `Program.cs:34/38/76`；`Migrations/` 下 23 个 .cs（ClouderyApi 7 + Mhop 13 + Identity 3） |
| 6 | 死 ModelState 7 分支 | MembersController.cs:34-35/44-45；WhitelistsController.cs:36-37；TermsController.cs:34-35/44-45；ApplicationsController.cs:34-35/44-45；测试佐证 `ClouderyApi.Tests/ClouderyMembersContractTests.cs:185` |
| 6 | 死并发 catch 4 处 | MembersAppService.cs:49；TermsAppService.cs:50；ApplicationsAppService.cs:49；ExamPaperAppService.cs:100（上下文无 IsRowVersion/[Timestamp]） |
| 6 | 直接绑定实体 | POST `ExamPapersController.cs:52`、PUT :68；`GetExamPaperFull` :33 |
| 6 | 读接口直接返回实体 | MembersController.cs:18/27、WhitelistsController.cs:22/30、TermsController.cs:18/27、ApplicationsController.cs:18/27（控制器现已返回 `*Out` 视图，「实体直返」在 Stage 2 M4 后已不存在）；输出视图已就绪：`Modules/Cloudery/Api/Contracts/MemberOut.cs:9`、`Modules/Zhuxs/Api/Contracts/{WhitelistOut.cs:7,TermOut.cs:9,ApplicationOut.cs:9}`、`Modules/Cloudery/Api/Contracts/ExamPaperDtos.cs:10-32` |
| 7 | 工程化现状 | 源码树无 `.editorconfig`（仅 obj 生成）；`ClouderyApi.csproj` 无 analyzer/TreatWarningsAsErrors；`.github/workflows/dotnet.yml` 已有 build+test 门禁；限流 `Program.cs:210-230` 与 `Shared/Filters/IpRateLimitAttribute.cs:16/45-57` |

## 二、逐项方案

### 5.1 拆分 ClouderyApiContext → ClouderyContext + ZhuxsContext

- **拆分归属**：ZhuxsContext = `ZhuxsWhitelists/ZhuxsTerms/ZhuxsApplications`（`ClouderyApiContext.cs:12-14`）；ClouderyContext = `ClouderyMembers/ExamPapers/ExamResults`（:15-17）。转换器/索引按归属搬移（:22-33 归 Zhuxs，:35-51 归 Cloudery，:56-62 归 Cloudery）。三处连接串均为 `DefaultConnection`（`Program.cs:34/38/76`），库不拆、只拆上下文。
- **建议分两步**：**第一步只做代码级隔离** —— 新增 `IClouderyDbContext`/`IZhuxsDbContext`（或直接依赖 Stage 2 的 `Modules.Cloudery`/`Modules.Zhuxs` 应用服务，`Program.cs:63-69` 已注册 MembersAppService/ExamPapersAppService/ExamResultAppService/WhitelistsAppService/TermsAppService/ApplicationsAppService），控制器改为只依赖接口/服务，**零迁移**。**第二步物理拆分** DbContext。
- **迁移与历史数据处置（第二步）**：现有 3 个 ClouderyApi 迁移迁往 ClouderyContext（`Migrations/ClouderyApi/20260906122952_AddExamPapers`、`20260906124754_mssql.local_migration_995`（空 Up/Down）、`20261001091128_AddExamResults`）。**关键事实：`ZhuxsWhitelists/ZhuxsTerms/ZhuxsApplications/ClouderyMembers` 四张表在模型快照里（`ClouderyApiContextModelSnapshot.cs:88/112/131/156` → 表名 :109/128/153/167）但没有任何迁移创建它们**，测试端靠 `ClouderyApi.Tests/TestSupport/ClouderyApiFactory.cs:66` 的 `GenerateCreateScript()` 直接建表。故需为 ZhuxsContext 生成一个 **幂等 baseline 迁移**（`CREATE TABLE IF NOT EXISTS`，与 `Migrations/Identity/20261002053242_InitialIdentity.cs:17-26` 同一写法），ClouderyContext 同样补 ClouderyMembers baseline。
- **对外契约影响**：无。**是否需 EF 迁移**：第一步不需要；第二步需要（baseline 迁移，且要配 5.5 的历史表迁移）。**提交粒度**：接口隔离 1 个提交；拆上下文 + baseline 迁移 1–2 个提交（Zhuxs、Cloudery 各一）。

### 5.2 Options 模式替换配置直读

- **建议新增强类型类**（全部零契约影响，字段默认值必须与现状一致）：
  - `CasdoorSettings`（bind `Casdoor`，键见 `ClouderyApi/appsettings.json:16-25`）：Endpoint/OrganizationName/ApplicationName/ApplicationType/ClientId/ClientSecret/CallbackPath/Scopes(string[])；替换 `Modules/Identity/Api/AuthController.cs:34-43`、`Modules/Mhop/Application/CasdoorAppService.cs:31-41/105-108`。
  - `AdminOptions`（bind `Authorization`，appsettings.json:47-53）：`string[] Admins`；替换 `Shared/Filters/AdminOnlyAttribute.cs:26-30`（配合 5.3）。
  - `SckeyOptions`（bind `Env`，appsettings.json:12-15，保留 `SurvivalCraft:*` 回退与默认 `https://api.sckey.net`）：`ApiBase`、`BearerToken`；替换 `Modules/SurvivalCraft/Api/ServerController.cs:28-36`。
  - `CorsSettings`（bind `Cors`）：`string[] AllowedOrigins`；替换 `Modules/Mhop/Application/CasdoorAppService.cs:127`（`Program.cs:125` 的 CORS 装配可继续用 `GetSection`）。
  - 扩展现有 `Modules/Mhop/Infrastructure/MhopOptions.cs`：补 `AutoMigrate`、`Seed` 与 `Provider`、`UploadDir`，替换 `Program.cs:91/96/101/179/192/274` 的裸键读取。
- **不改**：`LlmOptions` 已由 `Program.cs:42-51` 正确绑定（含 `Mhop:Llm` 回退）；`AddCasdoor(builder.Configuration.GetSection("Casdoor"))`（Program.cs:113）是第三方扩展，保持。
- **对外契约影响**：无（只换读取方式）。**EF 迁移**：无。**提交粒度**：按配置节各 1 个提交（Casdoor / Authorization / SCKEY / Cors / Mhop 补充）。

### 5.3 基于 policy 的授权替换 AdminOnlyAttribute

- **现状**：`AdminOnlyAttribute` 用 service-locator 取 `IConfiguration`（:26-30），未认证写 401 `{"success":false,"message":"请先登录"}`（:19-23），非管理员写 403 `{"success":false,"message":"无管理员权限，操作被拒绝"}`（:32-39）。
- **建议**：`builder.Services.AddAuthorization(o => o.AddPolicy("AdminOnly", p => p.RequireAuthenticatedUser().AddRequirements(new AdminRequirement())))` + `IAuthorizationHandler`（注入 `IOptions<AdminOptions>`，读 claim `CasdoorId`，OrdinalIgnoreCase）；把 14 处 `[AdminOnly]` 换成 `[Authorize(Policy = "AdminOnly")]`（清单见证据表 #3），删除过滤器。
- **401/403 逐字保留的关键**：默认 policy challenge 走 Bearer → **空体 401 + `WWW-Authenticate: Bearer`**，会丢掉 `{success,message}`。必须同时注册自定义 `IAuthorizationMiddlewareResultHandler`：
  - 未认证 → 401，body `{"success":false,"message":"请先登录"}`；
  - 已认证非管理员 → 403，body `{"success":false,"message":"无管理员权限，操作被拒绝"}`。
  - 现状语义差异：类级 `[Authorize]` 的 4 个控制器（Members/Whitelists/Terms/Applications）未登录时由中间件先挑战，**401 空体**；只有无类级 `[Authorize]` 的 `ExamPapersController`（:14）未登录才由过滤器写 401 JSON。自定义 handler 需据 endpoint 元数据区分这两类（有普通 `[Authorize]` 时维持空体/Bearer，仅 AdminOnly policy 时写 JSON）——此判定细节标「待实测」。
- **对外契约影响**：若不加自定义 handler，**会改变 401/403 形状**（列入第三章）。**EF 迁移**：无。**提交粒度**：1 个提交 + 契约测试（复用 `CookieAuthorizationContractTests` 基线）。

### 5.4 启动期 Migrate/Seed 移出

- **现状**：`Program.cs:179-190` 调 `MhopDbContext.Database.Migrate()`（:184），失败仅 `LogWarning`（:188）；`:192-206` 调 `MhopSeeder.SeedAsync`，失败仅 `LogWarning`（:204）。`ClouderyApiContext`/`IdentityDbContext` 启动期**从不迁移**。
- **建议**：抽 `DatabaseMaintenanceService`（Migration + Seed 两方法），新增 CLI 开关 `--migrate`/`--seed`（仓库已有 `--sweep-orphans` 先例，`Program.cs:171-175`），由 `.github/workflows/deploy.yml` 在 `docker restart` 前执行；`Mhop:Seed` 默认改 false（现 appsettings.json:63 为 true）。若部署环境不能跑 CLI，保留开关控制的 `IHostedService` 受控后台任务，禁止无条件启动迁移。
- **迁移执行清单（全量 10 个）**：ClouderyApi 3（`20260906122952_AddExamPapers`、`20260906124754_mssql.local_migration_995`（空）、`20261001091128_AddExamResults`）；Mhop 6（`20260924162305_MhopInitial`、`20260925165038_MhopUserPermissions`、`20260930124306_MhopBottles`、`20261001035824_mssql.local_migration_832`、`20261001042631_MhopAiAutoReview`、`20261001045007_MhopBottleMessageAiReview`）；Identity 1（`20261002053242_InitialIdentity`）。
- **幂等策略**：Seed 已幂等（`MhopSeeder.cs:24/49/62` 存在性检查，:40-42 仅升级角色）；迁移一律**前滚**，**禁止依赖 `Down()`** —— `InitialIdentity.cs:30-34` 的 Down 是刻意的 no-op（防误删账号）。
- **对外契约影响**：无（仅启动时序/运维）。**EF 迁移**：无。**提交粒度**：抽取服务 + 开关 1 个提交，部署脚本 1 个提交。

### 5.5 每上下文独立 MigrationsHistoryTable

- **现状风险**：三上下文同库同连接、无 `MigrationsHistoryTable` 配置，全部写入同一张 `__EFMigrationsHistory`。后果：无法按上下文独立前滚/回滚；`migrations list` 结果混杂；迁移命名误导（`20260906124754_mssql.local_migration_995` 实为空迁移、`20261001035824_mssql.local_migration_832` 名带 mssql 实为 MySQL）；且 ClouderyApi 历史表只有 3 行，却有 4 张表在 EF 之外创建（5.1）。
- **切换步骤**：① 三处 `UseMySQL`（`Program.cs:34/38/76`）与 2 个设计时工厂、以及为 ClouderyApiContext 新加的工厂，统一加 `.MigrationsHistoryTable("__EFMigrationsHistory_<Context>")`；② 对既有生产库执行一次性数据搬迁：新建 4 张历史表，按已知 MigrationId 列表从旧表 `INSERT ... SELECT`；③ 搬迁在同一发布窗口内完成，之后一律前滚。
- **对既有生产库影响**：仅新增历史表 + 拷贝行，不动业务表；但必须与 5.1 第二步同批上线，否则新上下文会误判迁移未应用并重复执行 baseline。**EF 迁移**：无新增迁移（配置 + 数据修复）。**提交粒度**：1 个提交（随上下文拆分）。

### 5.6 死代码清理与 ExamPaper 输入 DTO

- **死 ModelState 分支（7 处）**：`MembersController.cs:34-35/44-45`、`WhitelistsController.cs:36-37`、`TermsController.cs:34-35/44-45`、`ApplicationsController.cs:34-35/44-45`。`[ApiController]` 在进入 action 前已用 `ValidationProblemDetails` 短路（全仓库无 `ConfigureApiBehaviorOptions`），分支不可达。删除属**观察上无变化**；但**若**改为手工返回 400 `{success,message}` 则会改变形状 → 需批准。
- **死并发 catch（4 处）**：`Modules/Cloudery/Application/MembersAppService.cs:49`、`Modules/Zhuxs/Application/TermsAppService.cs:50`、`Modules/Zhuxs/Application/ApplicationsAppService.cs:49`、`Modules/Cloudery/Application/ExamPaperAppService.cs:100`。上下文无并发令牌（`ClouderyApiContext.cs:19-63`），`DbUpdateConcurrencyException` 不可达；删除无观察影响（ExamPapers 控制器 :73 的 409 文案随之消失，故仍归入需评估项）。
- **读接口实体直返 → 输出视图**：改用已存在的 `MemberOut/WhitelistOut/TermOut/ApplicationOut`（证据表 #6 行号；属性声明顺序即 JSON 键顺序，必须逐字保持）。
- **ExamPaper over-posting 修复**：`GetExamPaperFull`（:33）改用 `ExamPaperFullView`（`Modules/Cloudery/Api/Contracts/ExamPaperDtos.cs:10-32` 已就绪）；新增 `ExamPaperInput { Name, Sections }`，POST :52 / PUT :68 改为绑定输入 DTO，Id 由服务端生成、UpdatedAt 服务端覆盖、仅采纳 Name/Sections。
- **对外契约影响**：删除死代码无；实体直返与 ExamPaper DTO 改造**会改变响应/校验形状**（列入第三章）。**EF 迁移**：无。**提交粒度**：死代码 1 个；输出视图 1 个（逐控制器）；ExamPaper DTO 1 个（需先批准）。

### 5.7 .editorconfig + analyzer、CI 门禁、限流分布式化（可选）

- **.editorconfig + analyzer**：新增仓库根 `.editorconfig`（当前源码树只有 obj 生成件）；加 `Directory.Build.props` 设 `<AnalysisLevel>latest-recommended</AnalysisLevel>`，CI Release 构建加 `-warnaserror`（`ClouderyApi.csproj` 现无 TreatWarningsAsErrors；`ClouderyApiContext.cs:21` 的 CS8603 已有 pragma，可先保留）。**最小落地点：根 .editorconfig + CI `dotnet build -warnaserror`。**
- **CI 门禁**：`.github/workflows/dotnet.yml` 已含 restore/build/test + mysql:8.0 service，门禁**已达标**；可在其上加 `warnaserror` 与 `dotnet format --verify-no-changes`（可选）。
- **限流分布式化（可选）**：两条内存实现 —— `Program.cs:210-230`（`ConcurrentDictionary`，300 req/60s，429 `{"success":false,"message":"请求过于频繁，请稍后再试"}`）与 `Shared/Filters/IpRateLimitAttribute.cs:16/45-57`（每 IP+path，429 `{"success":false,"message":"分析请求过于频繁，请稍后再试","retryAfterSeconds":N}` + `Retry-After`，唯一使用点 `Modules/Cloudery/Api/ResultAnalysisController.cs:18`）。多实例部署下应换为 `AddRateLimiter` + Redis，`OnRejected` 必须逐字复刻上述两个 429 体与 `Retry-After`。
- **对外契约影响**：前两项无；限流换实现若文案/头有变则**会改变**（列入第三章）。**EF 迁移**：无。**提交粒度**：editorconfig/CI 1 个；限流 1 个（可选、独立）。

## 三、会改变对外行为的改造（需用户批准，须与冻结契约基线做冲突评估）

1. `GET /exam/ExamPapers/{id}/full` 改用 `ExamPaperFullView`（字段集/顺序若有差异即改变响应）。
2. `POST /exam/ExamPapers` 改用 `ExamPaperInput`：拒绝客户端指定 Id；400 校验错误的键集合由实体属性名变为 DTO 属性名。
3. `PUT /exam/ExamPapers/{id}` 改用 `ExamPaperInput`（同上）。
4. 读接口 `GET /cloudery/members`、`/zhuxs/{whitelists,terms,applications}` 改用 `*Out` 视图（属性顺序/可空性必须逐字一致，否则响应变化）。
5. 删除死 ModelState 分支：本身无观察变化；**但**若同时“修好”为手工 400 `{success,message}`，则把现有 400 `application/problem+json` 改成 `{success,message}` —— 属契约变更。
6. `AdminOnly` → policy：若未加自定义 `IAuthorizationMiddlewareResultHandler`，401/403 由 `{success,message}` 变空体/Bearer，且可能引入 302 —— 必须先加 handler 再切换。
7. 限流分布式化：若 429 体或 `Retry-After` 未逐字复刻即改变。
8. （迁移风险，非契约）上下文拆分的 baseline 迁移若用 `CREATE TABLE` 而非 `IF NOT EXISTS`，会在既有生产库直接报错。

## 四、执行顺序

1. **5.2 Options**（无迁移、无契约影响，先行解耦 `IConfiguration`）。
2. **5.3 policy 授权**（依赖 5.2 的 `AdminOptions`；先落自定义 result handler + 契约测试，再换属性）。
3. **5.6 死代码删除 + 输出视图**（输出视图部分需批准）。
4. **5.7 .editorconfig + CI**（独立可并行）。
5. **5.1 第一步接口隔离**（等 Stage 2 M4 控制器瘦身完成，避免行号/结构冲突）。
6. **5.1 第二步拆上下文 + 5.5 独立历史表**（同批上线，含 baseline 迁移与历史表搬迁）。
7. **5.4 Migrate/Seed 移出**（可在 5.1 之前或之后，独立发布流程变更）。
8. **5.7 限流分布式化**（最后，可选）。

## 五、验收

- `dotnet build` 0 error；`dotnet test` 全绿（单跑，勿与其它进程并发争 MySQL 连接）。
- `dotnet ef migrations list` 对 3（拆分后 4）个上下文均正常；无重复/空迁移。
- 契约对比：所有既有路由、请求/响应 JSON、401/403/400/404/429 形状与 `WWW-Authenticate`、`Retry-After` 逐字不变（特征测试 + swagger diff）。
- `grep`：`Modules/*/Api` 与 `Modules/*/Application` 中不再出现 `IConfiguration["..."]` 直读（Program.cs 装配除外）；不再出现 `GetService<IConfiguration>`。

## 六、风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| 3 上下文共享历史表，拆分时误判迁移已应用 | 高（重复建表/迁移失败） | 5.1 与 5.5 同批上线；baseline 用 `IF NOT EXISTS`；先备份 |
| Zhuxs/ClouderyMembers 表早于 EF，无迁移来源 | 中 | 用幂等 baseline 迁移标记，不重建表 |
| AdminOnly→policy 改变 401/403 形状 | 高（前端契约） | 先加自定义 `IAuthorizationMiddlewareResultHandler` + 契约测试，再切属性 |
| ExamPaper 输入 DTO 改变 400 键名/响应 | 中 | 需批准；用特征测试逐字段对比 |
| Stage 2 M4 进行中，控制器行号再变 | 中 | 涉及 `Modules/{Cloudery,Zhuxs}/Api/` 与 `Modules/Identity/Api/AuthController.cs` 的位置以最新 HEAD 复核 |
| 启动期 Seed 默认 true，多实例并发 | 中 | 移出到部署步骤；Seed 已幂等 |
| 本机 MySQL 连接紧张 | 中 | 测试单跑，不并发全量套件 |
