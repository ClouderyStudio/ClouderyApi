# ClouderyApi

ClouderyApi 是驱动 Cloudery 生态各站点后端的 ASP.NET Core Web API 服务（目标框架 **.NET 10**）。
它为多个关联子项目提供统一接口：云术（Cloudery）团队站、竹像素（Zhuxs）白名单与申请系统、SurvivalCraft 服务器接口、公益心理辅助平台 **MHOP**（匿名倾诉论坛 / AI 陪伴 / 心理量表评估 / 管理后台），以及若干通用工具接口。

## 功能概览

| 模块 | 路由前缀 | 说明 |
| ---- | -------- | ---- |
| 身份认证 | `/identity/auth` | 基于 **Casdoor** OAuth2 的登录 / 回调 / 登出 / 当前用户查询，Cookie 会话 + CSRF 防护；另有 `GET /config` 供第三方站点取登录元数据 |
| 团队成员 | `/cloudery/members` | 团队 / 组织成员信息（姓名、职位、简介、社交链接）增删改查 |
| 内部试卷 | `/exam/ExamPapers` | 内部测试试卷（心理学项目）整卷 JSON 存于 `ExamPapers` 表；公开读（**不含答案/解析**）+ `POST /{id}/grade` 服务端判分，写操作需管理员；`/exam/ExamPapers/{id}/full`（管理员）读取含答案全量 |
| 结果解读 | `/exam/result-analysis` | 量表结果的 AI 解读（按量表类型分流提示词），模型不可用时回退本地文本；公开接口，按 IP 限流 8 次 / 300 秒 |
| 云端结果 | `/exam/results` | 登录用户的测评结果云端存档（多平台共享）：列表 / 单条上传 / 批量同步 / 按 Id 或 clientKey 删除 / 清空，按 Casdoor 用户隔离；未登录返回 401 |
| 白名单 | `/zhuxs/whitelists` | 竹像素白名单（邀请码）管理 |
| 申请 | `/zhuxs/applications` | 入服申请审核（是否通过、申请时间、FAQ 问答） |
| 周目 | `/zhuxs/terms` | 周目信息（名称、起止时间、版本、模组数、人数、模组文件） |
| 服务器 | `/sc/server` | SurvivalCraft 服务器接口（转发 / 查询） |
| SCForge 资源平台 | `/scforge` | 生存战争插件、模组资源目录：浏览 / 搜索 / 详情 / 版本下载、发布资源与追加版本、评论与回复、资源与评论赞踩（写操作需 Casdoor 会话） |
| 长链 | `/misc/longlink` | 将普通链接编码为 IPv6.arpa 长链，解码并安全跳转（仅允许 http/https） |
| MHOP 公共 | `/mhop` | 健康检查、援助热线、在线人数心跳（匿名） |
| MHOP 认证 | `/mhop/auth` | 注册 / 用户名密码登录 / 邮箱验证码登录 / **Casdoor 统一身份登录** / 当前用户 / 资料与手机号绑定（登录后统一签发 **JWT Bearer**） |
| MHOP 论坛 | `/mhop/forum` | 板块、帖子、回复（先审后发）、**审核通过后**的 AI 自动回复、点赞；`/mine/*` 与作者自助编辑 / 撤回审核 / 重新提交 / 删除 |
| MHOP 漂流瓶 | `/mhop/bottles` | 投瓶 / 捞瓶 / 我的瓶子 / 瓶内消息 / 结束与举报；敏感词同步拦截，AI 初筛通过后入海 |
| MHOP 量表 | `/mhop/assessments` | PHQ-9 / GAD-7 / 自由倾诉：服务端计分 + AI 解读 |
| MHOP 后台 | `/mhop/admin` | 数据看板、帖子巡检与**删除**、回复审核与**删除**、AI 回复撤回/恢复/重新生成、用户管理与**删除**、AI 日志（需管理员） |
| MHOP 上传 | `/mhop/upload` | 头像 / 帖子图片上传；存储可切换本地磁盘（`/mhop/uploads/*`）或**远端阿里云 OSS** |

## 技术栈

- **ASP.NET Core**（net10.0），控制器 `[ApiController]` 风格 REST API
- **Entity Framework Core**，使用 **MySQL** 驱动（`MySql.EntityFrameworkCore`）
- 五个 `DbContext`：`ClouderyContext`（云术域：成员 / 试卷 / 成绩，含 JSON 列转换与 `ExamResults` 唯一索引）、`ZhuxsContext`（竹像素域：白名单 / 条款 / 申请，独立迁移历史表）、`IdentityDbContext`（身份域，本地登录用户 `Users` 表）、`MhopDbContext`（MHOP 域，表名统一加 `mhop_` 前缀以隔离，含点赞唯一索引与级联删除）、`ScforgeDbContext`（SCForge 资源平台域，表名统一加 `scforge_` 前缀，含 slug 唯一索引与投票去重唯一索引）
- **Casdoor** OAuth2 认证（`Casdoor.AspNetCore` + `Casdoor.Client`），Cookie 会话，会话有效期 7 天且支持滚动续期
- **MHOP 认证**：手写 HS256 JWT（`Authorization: Bearer`）+ PBKDF2-SHA256 密码哈希，兼容原有协议；并提供 **Casdoor 统一身份认证（OAuth2 授权码 / OIDC）** 登录，成功后同样签发 MHOP JWT
- **共享大模型客户端**：`Shared/Ai` 提供 OpenAI 兼容 `/chat/completions` 的 `ILlmClient` 与危机词 / 热线前缀 `CrisisSupport`，供 MHOP 与量表结果解读共用；配置见根级 `Llm`
- **SixLabors.ImageSharp** 处理上传图片：头像方形裁剪、帖子图缩放、统一 WebP 编码（对应 Python 版的 Pillow）
- 自定义 **CSRF 防护**中间件：对 POST/PUT/PATCH/DELETE 请求校验 Origin 头是否在 CORS 白名单内
- **Swagger / OpenAPI**（开发环境启用）
- **Costura.Fody** 将依赖程序集嵌入，便于单文件分发
- **模块化单体（DDD 分层）**：业务代码按限界上下文放在 `Modules/<Ctx>/`，模块内再分 `Domain`（聚合与领域规则）/ `Application`（用例编排与映射）/ `Api`（控制器与请求响应 DTO）/ `Infrastructure`（持久化与外部服务）；跨模块共享内核在 `Shared/`。改造历程见 [docs/DDD-REFACTOR-PLAN.md](docs/DDD-REFACTOR-PLAN.md)
- **xUnit 测试项目** `ClouderyApi.Tests/`：契约测试 + 领域单测，CI 以 `dotnet build -warnaserror` 与 `dotnet test` 为门禁（见下文「测试」）
- GitHub Actions 自动化构建，推送 `master` 自动部署到 1Panel（见 [DEPLOY.md](DEPLOY.md)）

## 目录结构

```
ClouderyApi/
├── Program.cs                     # 入口：服务注册、认证、CORS、CSRF 中间件、基础限流、Swagger、命令行开关
├── ClouderyApi.csproj             # 项目文件与 NuGet 依赖
├── appsettings.example.json       # 配置示例（提交到仓库）
├── appsettings.json               # 实际配置（含密钥，已被 .gitignore 忽略，不入库）
├── ClouderyApi.http               # HTTP 调试脚本（VS 使用）
├── Modules/                       # 业务代码：按限界上下文分模块，模块内再分 Domain / Application / Api / Infrastructure
│   ├── Cloudery/                  # 云术域：成员 / 试卷 / 云端测评结果 / 结果解读
│   │   ├── Api/                   # Members、ExamPapers、ExamResults、ResultAnalysis 控制器；Contracts/ 为请求响应 DTO
│   │   ├── Application/           # *AppService 用例编排、ExamResultService、ResultAnalysisService、IClouderyDbContext、Mapping/
│   │   ├── Domain/                # Member、ExamPaper、ExamPaperGrader、ExamResult
│   │   └── Infrastructure/Persistence/   # ClouderyContext + ClouderyContextFactory
│   ├── Zhuxs/                     # 竹像素域：白名单 / 周目 / 申请（结构同 Cloudery，含 IZhuxsDbContext 与 ZhuxsContext）
│   ├── Identity/                  # 身份域：AuthController、UserSyncService、User、IdentityDbContext
│   ├── Mhop/                      # MHOP 域（最大的模块）
│   │   ├── Api/                   # 11 个控制器 + MhopControllerBase / MhopAdminAttribute；Contracts/ 为 DTO
│   │   ├── Application/           # *AppService、内容审核与服务、权限码、Events/ 领域事件订阅者、Mapping/
│   │   ├── Domain/                # 聚合与领域规则（Post / Reply / Bottle / User、量表计分、内容策略、Events/）
│   │   └── Infrastructure/        # AI、JWT、密码哈希、邮件验证码、对象存储、在线人数、Seeder / 迁移维护 / 孤儿清理、Persistence/
│   ├── Link/                      # 长链（LongLinkController）
│   ├── SurvivalCraft/             # SurvivalCraft 服务器接口（ServerController）
│   └── Scforge/                   # SCForge 资源平台：插件 / 模组 / 版本 / 评论 / 投票
├── Shared/                        # 跨模块共享内核
│   ├── Ai/                        # ILlmClient / LlmClient / LlmOptions / CrisisSupport
│   ├── Authorization/             # AdminOnlyAttribute + AdminOnlyAuthorization（基于 policy 的管理员鉴权）
│   ├── Domain/                    # IDomainEvent 等领域事件基座与 DomainEventDispatcher
│   ├── Exceptions/                # DomainRuleException / MhopApiException / MhopApiExceptionFilter
│   ├── Filters/                   # IpRateLimitAttribute（按 IP + 路径限流）
│   ├── Json/                      # MhopJson（snake_case + 北京时间 +08:00）
│   ├── RateLimit/                 # IRateLimitStore（进程内 / Redis 两种限流计数实现）
│   ├── Online/                    # IOnlineTrackerStore（在线人数：进程内 / Redis ZSET 两种实现）
│   ├── Email/                     # IEmailCodeStore（邮箱验证码：进程内 / Redis+Lua 两种实现）
│   ├── Redis/                     # RedisConnection（全进程共用一条连接）+ Data Protection 密钥环落点
│   └── Options/                   # AdminOptions / CasdoorSettings / CorsSettings / SckeyOptions / RedisOptions
├── Migrations/                    # 各 Context 独立迁移目录：Cloudery/、Zhuxs/、Identity/、Mhop/、Scforge/
└── Properties/launchSettings.json # 开发启动配置（端口 5171 / 7288）
```

`ClouderyApi.Tests/` 与 `ClouderyApi/` 并列，是 xUnit 测试项目（契约测试 + 领域单测）；`TestSupport/` 提供集成测试夹具（一次性 MySQL 库、认证 Cookie、JSON 辅助）。

## 快速开始

### 环境要求

- [.NET SDK 10.0](https://dotnet.microsoft.com/download)
- MySQL（`MySql.EntityFrameworkCore` 驱动）
- 一个可用的 **Casdoor** 实例（用于登录）

### 配置

复制示例配置并按实际环境填写：

```bash
cp ClouderyApi/appsettings.example.json ClouderyApi/appsettings.json
```

主要配置项：

| 配置节 | 说明 |
| ------ | ---- |
| `ConnectionStrings:DefaultConnection` | 数据库连接字符串 |
| `Casdoor` | OAuth2 认证：Endpoint、组织名、应用名、ClientId、ClientSecret、回调路径等 |
| `Cors:AllowedOrigins` | 允许跨域的来源白名单（默认含 localhost 及各站点域名） |
| `Env:SCKEY_API_BASE`、`Env:SCKEY_BEARER_TOKEN` | Server 酱（SCKEY）推送配置 |
| `Authorization:Admins` | 管理员 CasdoorId 列表，用于白名单/申请/周目/成员等敏感写操作 |
| `TrustedProxies` | 可信反向代理（`Proxies` 精确 IP / `Networks` CIDR）。生产在 Nginx 之后必填，否则按 IP 的限流会把全站算作一个客户端；**绝不要信任任意来源**。留空 = 不启用。见下方「反向代理与限流」 |
| `Redis` | 可选的 Redis 后端（限流计数 / 在线人数 / 邮箱验证码 / Cookie 密钥环）：`ConnectionString` 留空 = 不接入，各功能走进程内或文件系统实现（本地开发 / CI / 集成测试零依赖）；填入（如 `127.0.0.1:6379,abortConnect=false`，云 Redis 通常再要 `password=***,ssl=true`）后状态跨实例共享、重启不清零。`KeyPrefix` 默认 `cloudery:`。见下方「限流计数 / 在线人数 / 验证码的存放（可选 Redis）」 |
| `DataProtection:KeysDirectory` | Cookie（Casdoor 登录态）密钥环目录，留空 = 内容根下的 `keys/`（1Panel 已挂载宿主目录，容器重建不丢）；配了 Redis 时密钥环改存 Redis、本项忽略 |
| `Mhop` | MHOP 模块：`Jwt`（密钥 / 有效期）、`Llm`（OpenAI 兼容大模型，留空走本地兜底）、`Smtp`（邮箱验证码，`Host` 留空为开发模式；`AllowInvalidCertificate` 生产保持 false）、`Casdoor`（统一身份登录开关与回调地址）、`LekeHotline`、`UploadDir`、`SeedAdminPassword`（种子超管口令，留空则随机生成并记日志）、`AutoMigrate` / `Seed`（生产建议 `false`，改用 CLI `--migrate` / `--seed`） |
| `Llm` | 结果解读与 MHOP 共用的大模型配置（OpenAI 兼容）：`BaseUrl` / `ApiKey` / `Model`（默认 `glm-4-flash`）/ `TimeoutSeconds`（默认 30）；留空时逐项回退到旧配置 `Mhop:Llm` |

> ⚠️ `appsettings.json` 包含数据库口令、Casdoor 客户端密钥等敏感信息，已被 `.gitignore` 排除，**请勿提交到仓库**。默认端口见 `Properties/launchSettings.json`（`http://localhost:5171`，HTTPS `https://localhost:7288`）。

### 运行

```bash
cd ClouderyApi
dotnet restore
dotnet run
```

开发环境下访问 Swagger 文档：`http://localhost:5171/swagger`

OpenAPI 描述文档（开发环境）：`http://localhost:5171/openapi/v1.json`

### 数据库迁移

五个 `DbContext` 各自维护迁移：`IdentityDbContext` 在 `Migrations/Identity/`，`ClouderyContext` 在 `Migrations/Cloudery/`（沿用共享的 `__EFMigrationsHistory`，避免重跑既有迁移），`ZhuxsContext` 在 `Migrations/Zhuxs/`（独立历史表 `__EFMigrationsHistory_Zhuxs`），`MhopDbContext` 在 `Migrations/Mhop/`，`ScforgeDbContext` 在 `Migrations/Scforge/`。生成并应用迁移：

```bash
dotnet ef migrations add <Name> --context IdentityDbContext --output-dir Migrations/Identity
dotnet ef database update --context IdentityDbContext

dotnet ef migrations add <Name> --context ClouderyContext --output-dir Migrations/Cloudery
dotnet ef database update --context ClouderyContext

dotnet ef migrations add <Name> --context ZhuxsContext --output-dir Migrations/Zhuxs
dotnet ef database update --context ZhuxsContext

dotnet ef migrations add <Name> --context MhopDbContext --output-dir Migrations/Mhop
dotnet ef database update --context MhopDbContext

dotnet ef migrations add <Name> --context ScforgeDbContext --output-dir Migrations/Scforge
dotnet ef database update --context ScforgeDbContext
```

> 首次部署 MHOP 模块前执行 `dotnet ef database update --context MhopDbContext` 创建 `mhop_*` 表。开发环境下 `Mhop:AutoMigrate` 默认为 `true`，启动时自动迁移；生产环境保持 `false`，改用 CLI `dotnet ClouderyApi.dll --migrate [--seed]`（`.github/workflows/deploy.yml` 在重启容器前自动执行，失败即中止部署）。`--migrate` 会自动发现并前滚**全部** `DbContext`（Cloudery / Zhuxs / Identity / Scforge / Mhop，见 `ClouderyApi/Shared/Persistence/DatabaseMigrationRunner.cs`），新增上下文无需改 CLI。

> **命令行开关**（`dotnet ClouderyApi.dll <开关>`，执行完即退出、不启动 Web 主机）：`--migrate` 应用待执行迁移、`--seed` 写入种子数据（可同时使用）、`--sweep-orphans [--delete-orphans]` 清理对象存储中的孤儿图片（见下文「历史孤儿清理」）。

> 内部试卷表迁移 `AddExamPapers` 仅新增 `ExamPapers` 表（整卷 JSON 存单列，兼容既有 schema）。存在多个 `DbContext` 时，`dotnet ef` 命令需显式指定 `--context`。

> 云端测评结果迁移 `AddExamResults` 仅新增 `ExamResults` 表与索引 `IX_ExamResults_UserId_ClientKey`（唯一）、`IX_ExamResults_UserId_SavedAt`，兼容既有 schema。

> 身份域 baseline 迁移 `InitialIdentity` 的 `Up()` 使用 `CREATE TABLE IF NOT EXISTS`：全新库正常创建 `Users` 表，历史库中已存在的用户表则原样保留（保留既有用户 Id 与测评结果归属）；其 `Down()` 有意不删表。

## 配置说明（Program.cs 要点）

- **认证**：Casdoor 登录流程 + Cookie 认证，Cookie 设置 `HttpOnly=true`（防 XSS 窃取会话）、`SameSite=None`、`Secure`，有效期 7 天（滑动续期），登录 / 登出路径为 `/identity/auth/login`、`/identity/auth/logout`。
- **CORS**：名为 `AllowAllOrigins` 的策略，限定 `Cors:AllowedOrigins` 白名单，允许携带凭据，任意方法与请求头。
- **CSRF 防护**：非开发环境下启用自定义中间件，对跨站请求（Origin 不在白名单内）的写操作返回 403。
- **仅开发环境**：映射 OpenAPI 与 Swagger UI。

## 认证流程（Casdoor）

1. 前端调用 `GET /identity/auth/config` 获取 Casdoor 元数据（Endpoint / clientId / scope）与本服务的回调地址；
2. 前端调用 `GET /identity/auth/state` 获取一次性 state（服务端种下 `oauth_state` Cookie）；
3. 前端以 `redirect_uri` 指向其本站回调页，跳转 Casdoor 登录，登录后浏览器携带 code 与 state 返回；
4. 前端调用 `POST /identity/auth/callback`（JSON：code / state / redirectUri）完成换号与建会话，服务端校验 state（防登录 CSRF）；
5. 后续请求通过 Cookie 会话访问受限接口，`GET /identity/auth/me` 可获取当前用户。

> 回调必须携带与 `oauth_state` Cookie 一致的 `state`，否则拒绝登录（防 CSRF）。回调端点为 `HttpPost`，需由前端发起，而非浏览器直接跳转到该地址。

## 测评结果 AI 解读（`/exam/result-analysis`）

`psychology` 站点结果页调用的公开接口：把一次测评结果交给所配置的大模型，生成四段中文解读。匿名可用、无需登录，返回**裸对象**（Cloudery 模块风格，不用 `MhopOk` 的蛇形包装）：

```json
{ "analysis": "1) …2) …3) …4) …", "engine": "llm", "crisis": false, "generatedAt": "2026-01-01T00:00:00+00:00" }
```

### 请求体

| 字段 | 说明 |
| ---- | ---- |
| `testId` | **必填**，量表标识（如 `phq9`、`mbti`）；缺失返回 400 |
| `testTitle` / `category` | 量表名称与类别（症状 / 人格 / 专项） |
| `totalScore` / `maxScore` / `minScore` / `level` / `severity` / `scoreNote` | 本次得分口径与等级 |
| `suggestion` | 站点自带的量表解读，仅作为参考依据拼进提示词，不作为结论照抄 |
| `timeFrame` / `respondent` | 作答时间范围、作答人（本人自评 / 他人代答） |
| `risk` | 站点侧的风险标记 |
| `note` | 用户备注，**只有本人同意后才由站点下发**；服务端再截断 1200 字 |
| `scoreKind` | `severity` / `trait` / `type`，量表类型；缺省时服务端按 `testId` 与 `category` 关键词自行判定 |
| `profile` | 量表画像数据（名称 + 取值），如 MBTI 的四轴偏好 / 功能栈、七美德与七宗罪的指数、心理年龄双轴等 |
| `dimensions` | 维度名 / 分值 / 满分 / 等级 / 说明，最多 16 项 |

### 行为

- **提示词按量表类型分流**（`ResultAnalysisService.KindInstruction`）：类型型直接给出类型与画像、不出现「得分为 X/Y 分」；特质型禁用「正常 / 异常」「严重 / 轻度」「需要治疗」等病理化措辞；计分型用总分与等级说明程度，并要求剖析 2-3 个相对突出的维度。硬性要求至少引用三个具体数据点、禁止空话与照抄量表自带解读，全文 600 字以内。
- **危机处理**：`risk`、`level`、`scoreNote`、`note` 任一处命中危机词即前置援助热线文本（全国心理援助热线 12356、北京心理危机研究与干预中心 010-82951332，紧急情况 110/120），该文本与 MHOP 模块共用同一份 `CrisisSupport` 常量。
- **降级**：模型未配置、调用失败或返回空文本时，改用服务端本地规则文本（`engine = "local"`），接口不会以 5xx 结束。
- **限流**：`[IpRateLimit(MaxRequests = 8, WindowSeconds = 300)]`，按「IP + 路径」固定窗口计数，超限返回 429 与 `Retry-After`（响应体 `{ success, message, retryAfterSeconds }`）。

### 相关代码与配置

| 位置 | 说明 |
| ---- | ---- |
| `Shared/Ai/LlmOptions.cs` | 根级 `Llm` 配置：`BaseUrl` / `ApiKey` / `Model`（默认 `glm-4-flash`）/ `TimeoutSeconds`（默认 30） |
| `Shared/Ai/LlmClient.cs` | OpenAI 兼容 `/chat/completions` 调用；失败只记日志并返回 `engine = "local"`，不抛给调用方 |
| `Shared/Ai/CrisisSupport.cs` | 危机词、热线号码与热线前缀文本（与 MHOP 共用） |
| `Modules/Cloudery/Application/ResultAnalysisService.cs` | 提示词组装（`BuildSystemPrompt` / `BuildUserPrompt`）与本地兜底文本 |
| `Modules/Cloudery/Api/ResultAnalysisController.cs` | 路由 `POST /exam/result-analysis` |
| `Shared/Filters/IpRateLimitAttribute.cs` | 按 IP + 路径的固定窗口限流过滤器 |

> 根级 `Llm` 留空时会**逐项回退**到旧配置 `Mhop:Llm`；两者都空则结果解读一律走本地兜底（`engine = "local"`）。MHOP 的 AI 回复与审核也改用同一个 `ILlmClient`，`MhopAiService.ChatAsync` 的签名与 `Engine` 标记保持不变。

## 云端测评结果同步（`/exam/results`）

`psychology` 站点登录后的测评结果云端存档，支撑「多平台共享测试结果数据」：站点本机（localStorage）记录与云端按 `clientKey` 一一对应，换设备登录后即可把历史补齐。接口要求 **Casdoor Cookie 会话**（同 `/identity/auth`），用户身份取自 `ClaimTypes.NameIdentifier`，不同用户的记录互相隔离。

| 方法 | 路由 | 说明 |
| ---- | ---- | ---- |
| `GET` | `/exam/results` | 当前用户全部记录，返回 `{ success, total, results }` |
| `POST` | `/exam/results` | 上传单条记录（upsert） |
| `POST` | `/exam/results/sync` | 批量上传并回传云端全量；`records` 为空即「只取回」 |
| `DELETE` | `/exam/results/{id}` | 按记录 Id **或** `clientKey` 删除；不存在返回 404 |
| `DELETE` | `/exam/results` | 清空当前用户全部云端记录，返回 `{ success, deleted }` |

### 请求与响应

```json
// POST /exam/results/sync
{ "records": [ { "clientKey": "test_phq9_result_1759300000000", "testId": "phq9",
                 "testTitle": "PHQ-9", "savedAt": "2026-10-01T09:46:40.000Z",
                 "payload": { "testId": "phq9", "totalScore": 12 } } ] }

// 200
{ "success": true, "uploaded": 1, "total": 3,
  "results": [ { "id": "…", "clientKey": "…", "testId": "phq9", "testTitle": "PHQ-9",
                 "savedAt": "…", "updatedAt": "…", "payload": { … } } ] }
```

### 行为

- **幂等 upsert**：同一用户下 `clientKey` 唯一（唯一索引 `IX_ExamResults_UserId_ClientKey`，MySQL 允许多个 NULL），重复同步不会产生重复记录；`clientKey` 缺省时以 `testId@<savedAt ISO>` 兜底。
- **防旧设备回灌**：已存在的记录只有在新 `savedAt` **不早于**已存 `savedAt` 时才覆盖，晚到的旧设备不会把新结果改回旧数据。
- **配额**：单次最多 200 条、单用户只保留最新 200 条（超量自动裁剪）、单条 `payload` 上限 256 KB；超限返回 400 `{ "detail": "一次最多同步 200 条记录，请分批上传" }`。
- **时间**：`savedAt` / `updatedAt` 一律归一化为 UTC 后入库（MySQL `datetime` 不保留时区），对外返回 ISO 8601 北京时间（`+08:00`，见 `ClouderyApi/Shared/Time/BeijingTime.cs`）。
- **错误**：未登录 `401 { "detail": "未登录，无法使用云端同步" }`；`payload` 缺失或记录超限 `400`；目标记录不存在 `404`。错误体统一为 `{ "detail": "..." }`（不经 `MhopOk`，见 [docs/API-ERROR-SHAPE.md](docs/API-ERROR-SHAPE.md)）；成功体仍是裸对象。
- 路由**未挂** `[Authorize]`：为统一返回中文 401 体，鉴权在控制器内手动完成（`TryGetUserId`）。

### 相关代码与配置

| 位置 | 说明 |
| ---- | ---- |
| `Modules/Cloudery/Domain/ExamResult.cs` | 实体：`UserId` / `ClientKey` / `TestId` / `TestTitle` / `SavedAt` / `Payload`（`longtext`）/ `CreatedAt` / `UpdatedAt` |
| `Modules/Cloudery/Api/Contracts/ExamResultDtos.cs` | `ExamResultIn` / `ExamResultSyncIn` / `ExamResultOut` / `ExamResultSyncOut` |
| `Modules/Cloudery/Application/ExamResultService.cs` | 列表 / 同步 / 删除 / 清空，含配额校验与时间归一化 |
| `Modules/Cloudery/Api/ExamResultsController.cs` | 路由 `exam/results` |
| `Migrations/Cloudery/20261001091128_AddExamResults.cs` | 仅新增 `ExamResults` 表与两个索引，兼容既有 schema |

> 部署前执行 `dotnet ef database update --context ClouderyContext`。`ClouderyContext` / `ZhuxsContext` **不会**随启动自动迁移（只有 `MhopDbContext` 会自动迁移）。

## SCForge 插件、模组资源平台（`/scforge`）

> **SCForge 是云术工作室（Cloudery Studio）旗下项目** —— 品牌口径与官网一致：Copyright 2023-<year> Cloudery Studio, All rights reserved.

### 两类资源：插件 / 模组

平台上有两类资源，分属两块独立的板（`/plugins` 与 `/mods`），由 `ScforgePlugin.Kind` 区分：

| kind | 包格式 | 生效范围 |
| --- | --- | --- |
| `plugin` | `.dll` | 只在服务端加载 |
| `mod` | `.netmod` | **会随服务器下发到客户端**（同样可以用它写插件逻辑） |

- 类型在创建时确定（`POST /scforge/plugins` 的 `kind`），**创建后不可更改**；
  追加版本、替换版本文件时都会校验包扩展名与资源类型一致，不一致返回 400 并说明该传什么。
- 浏览接口支持 `?kind=plugin|mod` 过滤（非法值忽略，避免旧链接报错），facets 里带两块的计数。
- 平台因此不再只服务于服务端，对外文案统一为**生存战争插件、模组资源平台**。

### 游戏版本（超管可维护）

生存战争用日期式编号（`x26.07.01` / `x26.06.19` / `x26.05.23`），每两三周就往前推一格 ——
写死在代码里意味着每次都要改代码发版，所以放进 `scforge_game_versions` 表：

| 端点 | 权限 | 说明 |
| --- | --- | --- |
| GET `/scforge/game-versions` | 匿名 | 受支持版本（新的在前），发布页与筛选面板的取值来源 |
| GET `/scforge/admin/game-versions` | 管理员 | 后台列表，附带被多少资源引用 |
| POST `/scforge/admin/game-versions` | **超管** | 添加版本（可标 `beta`=内测）；格式必须是 `x26.07.01` 这类，重复返回 409 |
| DELETE `/scforge/admin/game-versions/{id}` | **超管** | 删除版本；仍被资源引用时拒绝 |

- 插件与版本的「兼容游戏版本」校验**以这张表为准**，新加的版本立刻可选，不需要发版。
- 初始三个版本由迁移 `ScforgeGameVersions` 写入；`ScforgeCatalog.DefaultGameVersions` 只是初始数据与离线回退。
- 前端在后台提供了「游戏版本」页（仅超管）：添加 / 标内测 / 删除，并显示每个版本的引用数。

SCForge 是生存战争（SurvivalCraft）插件、模组资源平台，动线对齐 Modrinth / CurseForge 的核心部分：
浏览与搜索插件、查看详情与版本、下载插件包、发布插件与追加版本、评论（含回复）以及插件 / 评论的赞踩。
内容采用**先审后发**，并配有管理员后台面板与两级角色（超级管理员 / 管理员）。
配套前端仓库为 **scforge-frontend**（Vue 3 + Vite，复用云术官网的 MD3 组件库），本模块提供其全部接口。

### 认证

沿用 Cloudery 主站的 **Casdoor Cookie 会话**（`/identity/auth/*`），不签发第二套令牌。
写接口不挂 `[Authorize]`，而是在控制器内判断登录态，因此未登录返回中文 401 裸对象
`{"success":false,"message":"请先登录"}`（与 Cloudery / Zhuxs 一致，而不是框架默认的 401 空响应体）。
### 审核流程（先审后发）

插件与版本各自持有状态 `ScforgeContentStatus`：`pending` / `published` / `rejected`。

| 动作 | 结果 |
| --- | --- |
| 发布新插件 | 插件与首个版本都进入 `pending` |
| 追加版本 | 该版本 `pending`；已通过的旧版本不受影响，插件本身仍在架 |
| 作者编辑插件资料 | 插件回到 `pending`（审核通过前公众看不到） |
| 作者修改版本元数据 / 替换版本文件 | 该版本回到 `pending` |
| 删除版本 | 直接生效，无需审核（最后一个版本不允许删） |
| 审核通过 / 驳回 | 由有 `review` 权限的管理员操作；**驳回必须给出理由**，理由会展示给作者 |
| 作者「重新提交审核」 | 把被驳回的插件 / 版本重新置为 `pending` |

**公开可见的判定**：插件 `published` **且至少有一个版本 `published`**。
作者本人与管理员可以预览未通过的内容（管理员需要它来做审核）；
其余调用者对未通过内容一律得到 404，不泄露「这里有个待审插件」。

### 角色与权限

后台身份存在 `scforge_admins` 表里，按 **Identity 域的用户 Id** 关联：

| 角色 | 能力 |
| --- | --- |
| 超级管理员 `super` | 拥有全部权限，并且是**唯一**能指定 / 调整 / 撤销管理员的人 |
| 管理员 `admin` | 由超管指定，只拥有被勾选的权限码 |

可授予的权限码（`ScforgePermissionSet`）：

| 权限码 | 界面名 | 说明 |
| --- | --- | --- |
| `review` | 审核权限 | 处理待审核的插件提交与版本文件，可通过或驳回并附理由 |
| `content` | 内容管理 | 编辑任意插件的资料与版本、删除插件（不参与审核流程） |

- **引导**：`Authorization:Admins`（CasdoorId 白名单）里的账号即使没有库记录也按**超级管理员**处理，
  因此第一个超管不必手工插库就能登录后台授权他人；白名单**优先于**库记录，避免一条误建的管理员记录把引导超管降级。
- **自我保护**：超管不能撤销自己的超管身份，系统也不允许移除最后一位超管。
- **发布权只属于作者**：改资料、发新版本、删除都由作者身份把关，管理员不在此生效 ——
  用管理员账号替别人发布会把内容记到原作者名下（冒名发布）；管理员的内容管理走独立的后台接口。
- 接口用 `canManage`（是否作者本人）、`canReview`、`canManageContent` 三个字段把这件事告诉前端；
  `GET /scforge/admin/me` 返回当前用户的后台身份与权限目录（非管理员返回 `isAdmin:false`，不报错）。

### 路由

| 方法 | 路由 | 认证 | 说明 |
| --- | --- | --- | --- |
| GET | `/scforge/game-versions` | 匿名 | 受支持的游戏版本（新的在前），发布与筛选的取值来源 |
| GET | `/scforge/plugins` | 匿名 | 分页搜索：`q` / **`kind`** / `category` / `tag` / `gameVersion` / `sort` / `page` / `pageSize`，同时返回 facets（含 kinds 计数） |
| GET | `/scforge/plugins/featured` | 匿名 | 首页精选（可带 `kind`）：显式 `Featured` 优先，不足按净评分补齐 |
| GET | `/scforge/plugins/recent` | 匿名 | 最近更新（可带 `kind`） |
| GET | `/scforge/plugins/{idOrSlug}` | 匿名 | 详情（GUID 或 slug），带 `canManage` / `canReview` / `canManageContent`；未通过审核对非作者与管理员返回 404 |
| GET | `/scforge/plugins/mine` | 登录 | 我提交的插件（含待审核与已驳回） |
| GET | `/scforge/plugins/mine/summary` | 登录 | 我的插件统计卡（含三种审核状态的数量） |
| POST | `/scforge/plugins` | 登录 | 发布新资源（multipart，`kind`=`plugin`/`mod`）；提交后进入待审核 |
| PUT | `/scforge/plugins/{id}` | **仅作者**（或 `content` 权限） | 编辑插件资料（multipart，可替换图标 / 截图）；编辑后回到待审核 |
| POST | `/scforge/plugins/{id}/resubmit` | **仅作者** | 被驳回后重新提交审核 |
| DELETE | `/scforge/plugins/{id}` | **仅作者**（或 `content` 权限） | 删除插件及其版本、评论与投票 |
| POST | `/scforge/plugins/{id}/versions` | **仅作者** | 追加版本（multipart）；新版本进入待审核 |
| GET / PUT / DELETE | `/scforge/plugins/{id}/vote[/up\|/down]` | 登录 | 插件赞踩（幂等：重复点同一方向不重复计数） |
| GET | `/scforge/versions/{id}/download` | 匿名 | 下载插件包（累加下载计数；未过审版本仅作者与管理员可取） |
| PATCH | `/scforge/versions/{id}` | **仅作者** | 编辑版本元数据（渠道 / 日志 / 兼容版本 / 依赖）；回到待审核 |
| POST | `/scforge/versions/{id}/file` | **仅作者** | 替换插件包文件（multipart）；回到待审核 |
| POST | `/scforge/versions/{id}/resubmit` | **仅作者** | 被驳回的版本重新提交审核 |
| DELETE | `/scforge/versions/{id}` | **仅作者** | 删除版本（最后一个版本不允许删） |
| GET / POST | `/scforge/plugins/{pluginId}/comments` | 匿名读 / 登录写 | 评论树 / 发表评论与回复 |
| PATCH | `/scforge/comments/{id}` | 仅作者 | 编辑评论 |
| DELETE | `/scforge/comments/{id}` | 作者 / 管理员 | 删除评论（删除会连带删除其全部回复）；管理员可删他人评论用于巡查 |
| PUT / DELETE | `/scforge/comments/{id}/vote[/up\|/down]` | 登录 | 评论赞踩 |

后台面板（`/scforge/admin`）：

| 方法 | 路由 | 所需权限 | 说明 |
| --- | --- | --- | --- |
| GET | `/scforge/admin/me` | 登录 | 当前用户的后台身份与权限目录（非管理员 `isAdmin:false`） |
| GET | `/scforge/admin/summary` | 管理员 | 待审核 / 已发布 / 已驳回数量、累计下载、管理员数 |
| GET | `/scforge/admin/review/plugins` | `review` | 插件审核队列（默认 `pending`，最老的排前面） |
| GET | `/scforge/admin/review/versions` | `review` | 版本审核队列 |
| POST | `/scforge/admin/plugins/{id}/review` | `review` | 通过 / 驳回插件（驳回必须附理由） |
| POST | `/scforge/admin/versions/{id}/review` | `review` | 通过 / 驳回版本 |
| GET | `/scforge/admin/plugins` | 管理员 | 全量插件（含待审 / 已驳回，支持 `q` 与 `status`） |
| PUT | `/scforge/admin/plugins/{id}` | `content` | 内容管理：编辑任意插件资料 |
| DELETE | `/scforge/admin/plugins/{id}` | `content` | 内容管理：删除插件 |
| GET | `/scforge/admin/users` | 超管 | 按用户名 / 邮箱搜索可指定的用户 |
| GET / POST | `/scforge/admin/admins` | 超管 | 管理员列表 / 指定或调整管理员 |
| DELETE | `/scforge/admin/admins/{id}` | 超管 | 撤销管理员 |

响应一律为**裸对象**（`{ success, message, ... }`），字段名 camelCase，时间统一为北京时间（`+08:00`）；
业务异常输出 `{ success:false, message }`，状态码 400 / 401 / 403 / 404 / 409。

### 数据表与迁移

```
scforge_plugins   插件（slug 唯一索引；TagsText / GameVersionsText 为可下推过滤的管线化副本）
scforge_versions  版本（内容寻址的插件包 StorageKey + SHA-256）
scforge_comments  评论（ParentId 自引用组成回复树）
scforge_votes     投票去重表（UserId + TargetType + TargetId 唯一索引）
scforge_admins    管理员（UserId 唯一 + 角色 + 权限码 JSON）
scforge_game_versions  受支持的游戏版本（Version 唯一 + 排序权重 + 内测标记，超管可维护）
```

`ScforgeDbContext` 沿用共享的 `__EFMigrationsHistory`，因此迁移 Id 必须全局唯一。
与 `ClouderyContext` / `ZhuxsContext` 一样，它**不会**随启动自动迁移，部署前需显式执行
`dotnet ef database update --context ScforgeDbContext`。

### 上传与文件布局

插件包与图片的存放位置由 `Scforge:Storage:Provider` 决定，两种实现共用同一套白名单、命名与 sha256 口径
（因此同一个包在两种存储下的 `StorageKey` 完全一致，切换存储不会造成语义漂移）。

**local（默认）** —— 落本机磁盘，`Scforge:UploadDir`（默认 `scforge-uploads`）下分两层：

```
packages/        插件包：按 sha256 内容寻址，私有目录，只能经 /scforge/versions/{id}/download 获取
public/images/   图片：唯一被静态托管的部分，对应 /scforge/uploads/images/*
```

**oss（阿里云 OSS）** —— 对象键带 `Scforge:Storage:Oss:Prefix`（默认 `scforge`）前缀：

```
{prefix}/packages/{sha256}{ext}     插件包：**私有对象**，不设 ACL
{prefix}/images/{guid}.webp         图片：公共读，返回 PublicBaseUrl 外链，浏览器直连
```

- 插件包在 OSS 里刻意保持私有、**不**下发预签名外链：下载入口必须唯一，否则「下载计数」与
  「未过审版本不可下载」都会被绕过；下载仍由 `/scforge/versions/{id}/download` 流式转发。
- 内容寻址在 OSS 下用 `DoesObjectExist` 去重；上传前先校验坏包，避免占用远端存储。
- 图片统一由服务端解码重编码为 WebP（最长边 512），再按 `PublicRead` 设置对象 ACL；
  Bucket 已公共读或走 CDN 回源时该调用失败只记警告，不影响访问。
- 外链前缀优先取 `PublicBaseUrl`（CDN / 自定义域名），留空则按 `<bucket>.<endpoint>` 推导。

插件包只接受 `.dll`（插件）与 `.netmod`（模组）—— **生存战争没有 zip 形式的插件或模组**。
校验按包体魔数而不是只看后缀：`.dll` 必须以 PE 头 `MZ` 开头（把压缩包改成 `.dll` 会被拒绝），
`.netmod` 若以 zip 魔数 `PK\x03\x04` 开头则按 zip 严格校验、否则视为不透明容器原样接受。
图片会先用 ImageSharp 解码、缩到最长边 512 并统一重编码为 WebP，只接受 JPG / PNG / WebP / GIF
（**不接受 SVG**：同源 SVG 可携带脚本，属于存储型 XSS 面）。静态托管带 `X-Content-Type-Options: nosniff`。

### 插件包规范

生存战争只有两种包，**没有 zip 形式的插件或模组**：

| 格式 | 是什么 | 分发范围 |
| --- | --- | --- |
| `.dll` | 插件：编译出来的 .NET 程序集 | 只在服务端加载 |
| `.netmod` | 模组：同样可以写插件逻辑，但**会随服务器下发到客户端** | 服务端 + 客户端 |

- 服务端只做**格式校验**（`.dll` 必须带 PE 头 `MZ`；`.netmod` 本身若是 zip 则必须是完好的 zip），
  并在包是压缩容器时读取根目录（或唯一一层子目录）的 `manifest.json`，用于补全作者没填的名称与版本号。
- 包内其余内容既不解析也不执行；没有 `manifest.json` 不会导致上传失败。
- 前端按扩展名把版本标成「插件」或「模组」，模组会额外提示「会下发到客户端」——
  这是服主决定是否安装的关键信息。

### 配置

| 配置项 | 说明 |
| --- | --- |
| `Scforge:UploadDir` | 上传根目录，相对路径按内容根解析（默认 `scforge-uploads`） |
| `Scforge:MaxPackageBytes` | 单个插件包上限，默认 67108864（64 MB） |
| `Scforge:MaxImageBytes` | 单张图片上限，默认 4194304（4 MB） |
| `Scforge:MaxPageSize` | 列表单页最大条数，默认 60 |
| `Scforge:Storage:Provider` | `local`（默认）或 `oss`（别名 `aliyun`） |
| `Scforge:Storage:Oss:Endpoint` | 如 `oss-cn-hangzhou.aliyuncs.com`，可带 `https://` |
| `Scforge:Storage:Oss:Bucket` | 存储桶名 |
| `Scforge:Storage:Oss:AccessKeyId` / `AccessKeySecret` | 访问密钥；用 STS 时另填 `SecurityToken` |
| `Scforge:Storage:Oss:PublicBaseUrl` | 对外访问域名（CDN / 自定义域名）；留空按 Bucket + Endpoint 推导 |
| `Scforge:Storage:Oss:PublicRead` | 图片上传后设为公共读，默认 `true`（插件包始终私有） |
| `Scforge:Storage:Oss:Prefix` | 对象键前缀，默认 `scforge`，用于与同一 Bucket 内其它业务隔离 |

### API Key（命令行 / CI 发布）

给脚本用的机器凭据：带 `Authorization: Bearer scf_…` 调用写接口，就能代替作者发布插件与版本，
不必在 CI 里保存 Cookie。**与网页登录共用同一套作者身份与审核流程** —— Key 发布的作品同样进待审核。

```bash
# 签发（登录态下，Cookie）
curl -X POST https://api.cldery.com/scforge/api-keys \
  -H 'Content-Type: application/json' -b "$COOKIE" \
  -d '{"name":"发版机器人","scopes":["read","publish"]}'

# 用它发版
curl -X POST https://api.cldery.com/scforge/plugins \
  -H 'Authorization: Bearer scf_XXXX...' \
  -F package=@和平区域插件.dll -F kind=plugin -F name=和平区域插件 \
  -F slug=hpqy -F summary="区域内禁战" -F category=protection \
  -F gameVersion=x26.07.01 -F version=1.0.0 -F channel=release -F changelog="首版"
```

| 端点 | 说明 |
| --- | --- |
| `GET /scforge/api-keys/scopes` | 可授予的作用域清单（含中文名与说明） |
| `GET /scforge/api-keys` | 我的 Key 列表 |
| `POST /scforge/api-keys` | 签发（**响应里的 `token` 只出现这一次**） |
| `POST /scforge/api-keys/{id}/revoke` | 吊销自己的 |
| `POST /scforge/api-keys/{id}/rotate` | 轮换：吊销旧的 + 发一把同权限的 |
| `GET /scforge/admin/api-keys` | 全站列表（仅超管） |
| `POST /scforge/admin/api-keys` | 为指定用户签发（仅超管，**可含 `manage`**） |

**作用域**：`read` 读自己的列表 / `publish` 发布、编辑资料、追加或替换版本 / `manage` 删除与重提审核。
注意**编辑插件资料走 `publish` 而非 `manage`** —— 它和发布一样会产生"进入待审核"的后果；
`manage` 只留给不产生新内容的两类操作。自助申请只能拿到 `read` + `publish`；`manage` 必须由超管在后台签发。
作用域只约束机器凭据，**网页后台操作不受影响**。

**安全约定**：

- 库里**只存令牌哈希**（SHA-256），明文只在创建响应里出现一次，之后任何接口（含超管）都取不回来。
  令牌形如 `scf_<base64url>`，前缀便于在日志与流量里识别。
- 列表只显示前缀与掩码（`scf_a1b2…****`），够认出"哪一把"但反推不出令牌。
- 吊销是软删除（写 `RevokedAt` + 原因），保留行便于审计。
- **Cookie 会话优先**：已登录时忽略 Bearer，不会被 Key 覆盖真实身份。
- 校验失败**不直接拒绝**，由各写端点按既有约定返回 `401 {"success":false,"message":"请先登录"}`，
  对外错误形状不变。作用域不足返回 `403`，脚本据此能区分"密钥失效"和"权限不够"。

> 迁移：`ScforgeApiKeys`（`Migrations/Scforge`）新增 `scforge_api_keys` 表。
> 迁移 ID 沿用全局唯一约定（四个域共用 `__EFMigrationsHistory`）。

> 部署 SCForge 前端时需把其来源加入 `Cors:AllowedOrigins`：非开发环境的 CSRF 中间件按同一份白名单校验写请求的 `Origin`。

> 把 `Scforge:Storage:Provider` 改成 `oss` 并填好 `Scforge:Storage:Oss` 的四个必填项即可切到 OSS；
> 缺项时启动日志会给出提示，SCForge 的写接口会以明确错误拒绝，不影响其它模块。
> **已入库的旧 URL 不会自动迁移**：本地相对地址（`/scforge/uploads/...`）在切到 OSS 后不再可访问，
> 需要时把历史图片重新上传一遍即可（插件包不受影响，它只存 StorageKey，下载接口与存储无关）。

> 首次部署后需要一位超管：把其 CasdoorId 填进 `Authorization:Admins`（引导口子），
> 之后即可在后台「管理员」页把它换成正式的库记录、并授予其他人 `review` / `content`。

### 相关代码

| 位置 | 说明 |
| --- | --- |
| `Modules/Scforge/Domain/` | 聚合（Plugin / Version / Comment / Vote / Admin）、`ScforgeCatalog`、`ScforgeContentStatus`、`ScforgeAdminRoles`、`ScforgePermissionSet`、模块内业务异常 |
| `Modules/Scforge/Application/` | `ScforgePluginAppService` / `ScforgeCommentAppService` / `ScforgeVoteAppService` / **`ScforgeAdminAppService`**、`ScforgeActor`、`ScforgeAdminContext`、`IScforgeDbContext`、`IScforgeFileStore`、`Mapping/ScforgeMapper.cs` |
| `Modules/Scforge/Api/` | **四个控制器**（插件 / 版本 / 评论 / 后台）+ `ScforgeControllerBase`（裸对象与业务异常翻译）+ `Contracts/` |
| `Modules/Scforge/Infrastructure/` | `ScforgeDbContext`(+Factory)、**`Storage/` 下的 `LocalScforgeFileStore` / `OssScforgeFileStore` / `ScforgeFilePolicy` / `ScforgePackageInspector`**、`ScforgeCurrentUser`、`ScforgeAdminAccessor`、`ScforgeOptions`、`ScforgeUploadPaths` |
| `Shared/Directory/IUserDirectory.cs` + `Modules/Identity/Application/UserDirectory.cs` | 跨模块只读用户目录（后台按用户名 / 邮箱指定管理员） |
| `Migrations/Scforge/` | `ScforgeInitial`（四张表）、`ScforgeReviewAndAdmins`（审核字段 + `scforge_admins`） |
| `ClouderyApi.Tests/ScforgeContractTests.cs` | 13 个契约用例：待审不可见 / 审核通过后可见 / 编辑重回审核 / 驳回需理由 / 投票幂等 / 评论与回复 / 下载字节一致性 / review 与 content 权限边界 / 超管才能管管理员 / 图标类型校验 |

## MHOP 模块（从 Python FastAPI 后端迁移）

MHOP（公益心理辅助平台）原本是独立的 FastAPI + SQLAlchemy 后端，现已整体迁入本项目，
本实现将路由前缀由 Python 版的 `/api/*` 调整为 **`/mhop/*`**（请求 / 响应字段与错误体保持一致），MHOP 前端 `api/index.js` 的 `baseURL` 与 vite 代理已同步为 `/mhop`。

### 路由与认证

| 分组 | 路由 | 认证 |
| ---- | ---- | ---- |
| 公共 | `/mhop/health`、`/mhop/hotlines`、`/mhop/online/*` | 匿名 |
| 认证 | `/mhop/auth/*` | 注册 / 登录 / 邮箱验证码 / Casdoor 统一身份登录匿名；其余 `Authorization: Bearer <JWT>` |
| 论坛 | `/mhop/forum/*` | 读取匿名可访问；发帖 / 回复要求登录且已绑定手机号 |
| 量表 | `/mhop/assessments/*` | 提交匿名可用；`/mine` 需登录 |
| 后台 | `/mhop/admin/*` | 需 `role=admin`（`MhopAdminAttribute`） |
| 上传 | `/mhop/upload/*` | 需登录 |

- **认证方式**：MHOP 使用自带的 HS256 JWT + PBKDF2-SHA256 密码哈希（格式 `pbkdf2_sha256$轮数$盐$散列`），
  与 Cloudery 主站的 Casdoor Cookie 会话相互独立、互不影响；也可用 Casdoor 统一身份账号登录（见下节），
  服务端自动绑定 / 创建本地 `mhop_users` 账号后签发同一种 MHOP JWT。
- **序列化**：MHOP 控制器统一通过 `MhopJson.Options` 输出**蛇形字段名**与 **北京时间（`+08:00`）**；
  请求体用 `[JsonPropertyName]` 显式绑定蛇形键名。错误统一为 `{ "detail": "..." }`（与 FastAPI 一致，也是**全站**统一形状，见 [docs/API-ERROR-SHAPE.md](docs/API-ERROR-SHAPE.md)）。
- **AI**：`Modules/Mhop/Infrastructure/MhopAiService.cs` 调用任意 OpenAI 兼容 `/chat/completions`；
  未配置或调用失败时降级为内置共情式规则回复；任何引擎下检测到危机信号都会强制前置援助热线。
- **后台任务**：帖子**首次审核通过**时，通过独立 DI 作用域异步生成一条 AI 回复并写入 `mhop_ai_logs`（关联 `reply_id`，可在后台撤回 / 恢复）。
  待审核 / 草稿期间反复编辑不会产生任何 AI 回复；**隐藏后重新展示会沿用已有回复，不会重复调用大模型**，
  历史遗留的重复回复也会在此时收敛为最新的一条。需要换一份解读时用
  `POST /mhop/admin/posts/{id}/ai-reply/regenerate` 强制重新生成。
- **生产部署**：MHOP 站点若与 API 不同源，需把其来源加入 `Cors:AllowedOrigins`；非开发环境的 CSRF 中间件会校验写请求的 `Origin`。

### 统一身份认证（Casdoor / OAuth2 + OIDC）

MHOP 支持直接使用现有 Casdoor 统一身份账号登录，复用项目根部的 `Casdoor` 配置（Endpoint / 组织 / 应用 / ClientId / ClientSecret），
登录成功后仍签发 **MHOP 自己的 JWT**，前端令牌存取逻辑与用户名密码登录完全一致。

| 接口 | 说明 |
| ---- | ---- |
| `GET /mhop/auth/casdoor/config` | 返回 `enabled / endpoint / client_id / scope / redirect_uri`，供前端拼接授权地址 |
| `GET /mhop/auth/casdoor/state` | 生成 HMAC 签名的一次性 `state`（10 分钟有效、无状态，防 CSRF 登录） |
| `POST /mhop/auth/casdoor/callback` | 携带 `{ code, state, redirect_uri }`：换取 Casdoor 用户信息 → 绑定 / 创建本地账号 → 返回 `TokenOut` |

- 本地账号与统一身份账号通过 `mhop_users.CasdoorId`（唯一索引，可为 NULL）关联；同一邮箱会自动绑定到既有账号，不会重复建号。
- 开关与回调地址见 `Mhop:Casdoor`（`Enabled`、`RedirectUri`）；`RedirectUri` 留空时按请求 `Origin`（须在 `Cors:AllowedOrigins` 白名单内）推导为 `{origin}/auth/casdoor/callback`。
- ⚠️ **必须在 Casdoor 应用的 Redirect URIs 中登记该回调地址**（例如 `https://你的MHOP域名/auth/casdoor/callback`），否则 Casdoor 会拒绝授权。
- 前端：登录页新增「使用统一身份认证登录」按钮，回调页为 `/auth/casdoor/callback`。

### 数据表（`mhop_` 前缀，与其它已有域隔离）

```
mhop_users       用户（唯一索引：Username / Email / Phone）
mhop_posts       主题帖（Status 0=待巡检 1=正常 2=违规；Crisis 危机标记）
mhop_replies     回复（IsAi 区分 AI 回复；Recalled / RecallReason 撤回审计）
mhop_likes       点赞去重表（UserId + TargetType + TargetId 唯一）
mhop_assessments 量表评估记录（仅显式勾选 save_to_cloud 时落库）
mhop_ai_logs     AI 调用审计日志
```

迁移位于 `Migrations/Mhop/`，由 `MhopDbContextFactory` 提供设计时上下文，
可用 `dotnet ef migrations add <Name> --context MhopDbContext --output-dir Migrations/Mhop` 继续演进。

### 个人内容管理（作者自助）

个人主页（`/profile`）新增「我的内容管理」：可查看自己全部帖子 / 回复（含审核中、已驳回、草稿），
并支持编辑、取消审核、重新提交审核与删除。

内容状态（`mhop_posts.Status` / `mhop_replies.Status`）：

| 值 | 含义 | 作者可编辑 | 作者可删除 | 公开展示 | 后台可见 |
| -- | ---- | ---------- | ---------- | -------- | -------- |
| 0 | 待审核 | ✅ | ✅ | ❌ | ✅ |
| 1 | 已通过 | ❌（仅可删除） | ✅ | ✅ | ✅ |
| 2 | 已驳回 | ❌（仅可删除） | ✅ | ❌ | ✅ |
| 3 | 草稿（作者取消审核） | ✅ | ✅ | ❌ | ❌（仅作者可见） |

| 接口 | 说明 |
| ---- | ---- |
| `GET /mhop/forum/mine/summary` | 我的帖子 / 回复按状态汇总数量 |
| `GET /mhop/forum/mine/posts?status=` | 我的帖子分页列表（含 `editable` / `can_withdraw` / `can_submit` / `review_note`） |
| `GET /mhop/forum/mine/replies?status=` | 我的回复分页列表（附所属帖子摘要与帖子状态） |
| `PUT /mhop/forum/posts/{id}`、`PUT /mhop/forum/replies/{id}` | 编辑本人内容，仅待审核 / 草稿可改；编辑不触发 AI 回复（AI 回复只在审核通过时生成） |
| `POST /mhop/forum/posts/{id}/withdraw`、`.../replies/{id}/withdraw` | 取消审核：待审核 → 草稿 |
| `POST /mhop/forum/posts/{id}/submit`、`.../replies/{id}/submit` | 重新提交审核：草稿 → 待审核 |
| `DELETE /mhop/forum/posts/{id}`、`DELETE /mhop/forum/replies/{id}` | 删除本人内容（任意状态）；会一并清理其回复、点赞、AI 日志与图片 |

- 仅作者本人可操作，他人调用一律 404；草稿不进入公开列表、不计入后台统计，也不能被他人回复或点赞。
- 对已通过 / 已驳回内容调用编辑接口会返回 400「已通过审核的内容不可修改，仅可删除」。

### 后台管理接口（`/mhop/admin/*`，需 `role=admin`）

| 接口 | 说明 |
| ---- | ---- |
| `GET /mhop/admin/posts`、`GET /mhop/admin/replies` | 帖子巡检 / 回复审核列表（可按 `status` 过滤；匿名内容会带出真实作者与手机号） |
| `POST /mhop/admin/posts/{id}/moderate`、`.../replies/{id}/moderate` | 通过 / 驳回；帖子**首次**通过时生成 AI 自动回复 |
| `DELETE /mhop/admin/posts/{id}` | 删除任意帖子：连同其全部回复、点赞、AI 日志与图片一并清理 |
| `DELETE /mhop/admin/replies/{id}` | 删除任意回复：连同其点赞、AI 日志与图片一并清理 |
| `DELETE /mhop/admin/users/{id}` | 删除用户：连同其名下帖子（含他人对这些帖子的回复）、回复、点赞、AI 日志、头像与图片一并清理，不可恢复 |
| `POST /mhop/admin/posts/{id}/ai-reply/regenerate` | 强制重新生成 AI 自动回复（先清理旧回复，再调用一次大模型） |
| `POST /mhop/admin/replies/{id}/recall`、`.../restore` | 撤回 / 恢复 AI 回复 |
| `GET /mhop/admin/users` | 用户列表，附带 `post_count` / `reply_count`，便于删除前确认影响范围 |

- 删除类接口统一走 `MhopContentService`（作者自助删除与管理员删除共用同一实现），避免两处逻辑漂移漏清数据；
  顺序一律先落库、再尽力清理对象存储，删库失败时不会先把图片删掉。
- 删除当前登录账号、或删除系统最后一个管理员会被拒绝（400）。

### 图片存储（本地磁盘 / 远端阿里云 OSS）

图片处理仍统一由 ImageSharp 完成（校验 ≤5MB 与 MIME、头像居中裁剪 200×200、帖子图宽度 >800 等比缩放、
编码 WebP 质量 82），编码结果交由 `IMhopObjectStorage` 落地：

| Provider | 行为 | 返回的 URL |
| -------- | ---- | ---------- |
| `local`（默认） | 写入 `Mhop:UploadDir`（默认 `<内容根>/uploads`），由 `UseStaticFiles` 托管 | `/mhop/uploads/{avatars\|posts}/{guid}.webp` |
| `oss` | 官方 `Aliyun.OSS.SDK.NetCore` 调用 OSS `PutObject`（**OSS 自有签名协议，非 S3**），对象键加 `Prefix` 前缀 | `{PublicBaseUrl}/{prefix}/{avatars\|posts}/{guid}.webp`；未配域名时按 `<bucket>.<endpoint>` 推导 |

配置（`Mhop:Storage` / `Mhop:Storage:Oss`）：

| 键 | 说明 |
| -- | ---- |
| `Storage:Provider` | `local`（默认）或 `oss`（别名 `aliyun`） |
| `Storage:Oss:Endpoint` | 如 `oss-cn-hangzhou.aliyuncs.com`，可带 `https://` 前缀 |
| `Storage:Oss:Bucket` | Bucket 名称 |
| `Storage:Oss:AccessKeyId` / `AccessKeySecret` | 建议使用仅授权该 Bucket 的 RAM 子账号密钥 |
| `Storage:Oss:SecurityToken` | 仅 STS 临时凭证需要；长期密钥留空 |
| `Storage:Oss:PublicBaseUrl` | CDN / 自定义域名（如 `https://cdn.example.com`）；留空则按 Bucket + Endpoint 推导 |
| `Storage:Oss:PublicRead` | 上传后把对象设为公共读；Bucket 已公共读或走 CDN 回源时可设为 `false` |
| `Storage:Oss:Prefix` | 对象键前缀，默认 `mhop`，便于与同一 Bucket 内其它业务隔离 |

启用步骤：在 `appsettings.json` 填好 `Storage:Oss` 各项（该文件已被 `.gitignore` 忽略，密钥不会入库）→
把 `Storage:Provider` 改为 `oss` → 重启。配置缺失时**启动即报错并给出中文提示**；上传失败返回
`502 图片存储服务暂时不可用，请稍后重试`。切换后本地 `uploads/` 仍作为静态目录挂载，保证迁移前的历史图片继续可访问。

**图片回收**：内容真正落库删除后，`IMhopObjectStorage.DeleteAsync` 会清理对应对象——删除帖子（连同其全部回复）、
删除回复、编辑时移除的图片、更换 / 清空头像。清理是**尽力而为**的：地址不属于当前存储（历史遗留、域名变更、
外部图片）会被跳过，单张失败只记 `Warning` 日志并继续，不影响删除接口本身。

**历史孤儿清理**：`--sweep-orphans` 会把对象存储里的对象与 `mhop_posts.images` / `mhop_replies.images` /
`mhop_users.avatar` 比对，列出无人引用的图片。默认**只预览不删除**：

```bash
dotnet ClouderyApi.dll --sweep-orphans                   # 预览：只打印孤儿列表
dotnet ClouderyApi.dll --sweep-orphans --delete-orphans  # 确认无误后实际删除
```

比对按「子目录 + 文件名」进行，因此更换自定义域名 / CDN / Bucket 前缀不会把正常图片误判为孤儿；
也只处理 `posts/` 与 `avatars/` 两个子目录，不会触碰同一 Bucket 内其它业务的对象。
该命令在数据库迁移与种子数据之前返回，不会触发任何数据库变更。

### 与 Python 版的实现说明

- **图片上传**：与 Python 版行为一致——校验 ≤5MB 与 MIME（JPG/PNG/WebP/GIF），头像居中裁剪为 200×200 方图、
  帖子图宽度超过 800 时等比缩放，统一以 **WebP（质量 82）** 保存。差异有两点：实现库 Pillow →
  **SixLabors.ImageSharp**（纯托管，可被 Costura 嵌入）；落盘目标由写死本地目录改为可切换的 `IMhopObjectStorage`
  （本地磁盘或远端阿里云 OSS，见上一节）。
- **在线人数 / 邮箱验证码**：与 Python 版行为一致；默认同样是进程内实现，但填上 `Redis:ConnectionString` 即换成
  Redis（在线人数 ZSET、验证码 Lua 原子校验），多实例部署不必再改代码。见下方「限流计数 / 在线人数 / 验证码的存放（可选 Redis）」。
- **SMTP**：内置极简 SMTP 客户端，同时支持隐式 SSL（465）与 STARTTLS（587）。

### 默认账号

种子数据（`Mhop:Seed=true` 或 CLI `--seed`）会创建超级管理员 `admin` 与一条引导帖。

**口令来源**：读 `Mhop:SeedAdminPassword`；该配置留空时生成一次性随机强口令，
并只写进本次启动日志（`LogWarning`），请从日志取口令后立即登录改密。

代码里不再内置任何默认口令——写死的口令（如历史上的 `admin123`）随仓库公开即等同无口令，
后台可被任意人爆破。`MhopSeedAdminSecurityTests` 会在弱口令回归时直接让测试失败。

`Mhop:AutoMigrate` 在开发环境默认开启；`Mhop:Seed` 默认关闭，生产由部署脚本执行 `--seed`。

### 反向代理与限流

生产部署通常在 Nginx / 云负载均衡之后，此时应用看到的客户端 IP 是代理地址，
按 IP 的限流（全局 300 次/分钟、单接口 `IpRateLimit`、邮箱验证码频控）会退化成
「全站共用一个计数桶」——既挡不住攻击，又会误伤正常用户。

在 `TrustedProxies` 节显式声明可信代理即可修正：

```json
"TrustedProxies": {
  "Proxies": ["127.0.0.1"],
  "Networks": ["172.16.0.0/12"],
  "TrustForwardedFor": true,
  "TrustForwardedProto": true
}
```

两项都留空 = 不信任任何代理，沿用直连行为（本地开发适用）。**只填自己控制的代理地址**：
信任任意来源等于让攻击者自己伪造 `X-Forwarded-For`，每次请求换IP，限流形同虚设。

### 限流计数 / 在线人数 / 验证码的存放（可选 Redis）

限流计数（`IRateLimitStore`）、在线人数（`IOnlineTrackerStore`）与邮箱验证码（`IEmailCodeStore`）默认都放在进程内：
单容器部署足够，但**重启即清零**、多实例时各算各的（验证码还会因为请求落到不同容器而校验失败）。
填上 `Redis` 节即全部切到 Redis 共享状态（键前缀 `cloudery:`），Cookie 认证的密钥环也一并落到 Redis：

```json
"Redis": {
  "ConnectionString": "127.0.0.1:6379,abortConnect=false,connectTimeout=2000",
  "KeyPrefix": "cloudery:"
}
```

- 留空 = **完全不建立连接**，行为与接入 Redis 之前一致（CI 与集成测试因此不依赖 Redis）；生产可用
  环境变量 `Redis__ConnectionString` 注入，避免把口令写进配置文件。
- 全进程共用一条连接（`Shared/Redis/RedisConnection.cs`），四项功能不会各开一份连接池；连不上时按各自策略降级。
- **限流计数**：在 Lua 里原子完成（`INCR` + 首次 `EXPIRE`），窗口从该 IP 的第一次请求起算；
  Redis 暂停/断连时**降级为进程内计数（fail-open）**并记 warning——限流暂时退化，但正常请求不会变成 500。
  429 的响应体与文案逐字不变（`[IpRateLimit]` 仍是 `{ success, message, retryAfterSeconds }` + `Retry-After` 头）。
- **在线人数**：Redis ZSET（成员 = 客户端标识，分值 = 心跳毫秒时间戳），心跳写入、过期清理与计数在一条 Lua 里完成，
  窗口沿用 90 秒；连不上时同样 fail-open 回退进程内计数。
- **邮箱验证码**：验证码、60 秒重发间隔与 IP 每小时配额都在 Redis，校验与一次性消费（错 5 次作废）由 Lua 保证原子，
  多实例下同一个验证码不会因为落到不同容器而失效。**这一项是 fail-closed**：Redis 不可用时发送与校验直接拒绝
  （错误体与文案不变），宁可暂时登录不了，也不放宽频控、更不退回无验证码校验。
- **Cookie 密钥环（Data Protection）**：默认落内容根下的 `keys/`（容器里是 `/app/keys`，1Panel 已把宿主目录挂到
  `/app`，因此**容器重建不再让全体用户掉线**）；配了 Redis 则改存 Redis，多实例共用同一密钥环、只需登录一次。
  目录可用 `DataProtection:KeysDirectory` 指定（相对路径按内容根解析）；`keys/` 已写进 `.gitignore`，不会被提交。

## 统一错误响应体

所有 **≥ 400** 的响应，只要没有业务体，统一为 `{ "detail": "中文文案" }`；400 模型校验额外带 `errors`，429 额外带 `retryAfterSeconds`（与 `Retry-After` 头同源）。框架产生的空体 401 / 404 / 405 / 415 与未处理异常 500 由 `ApiErrorBodyMiddleware`（管道最前）补齐，控制器统一用 `ApiError.Result` 出口。

成功体、路由表与上游透传（`/server/{...}`）不变；完整映射表、四个产生路径、前端消费与历史决策见
[docs/API-ERROR-SHAPE.md](docs/API-ERROR-SHAPE.md)。

## 测试

`ClouderyApi.Tests/` 为 xUnit 项目：`*ContractTests` 覆盖 HTTP 路由 / JSON 契约 / 鉴权与错误体形状（`SwaggerRouteSnapshotTests` 守住路由表），`Domain*Tests` 覆盖领域规则。

```bash
# 集成测试需要一个可用的 MySQL：每个测试类自建一次性库，测试结束即删除
export CLOUDERY_TEST_MYSQL="server=127.0.0.1;port=3306;user=root;password=root"

dotnet test ClouderyApi.sln --configuration Release                        # 全量
dotnet test ClouderyApi.Tests --configuration Release \
  --filter "FullyQualifiedName~ExamPapersContractTests"                    # 只跑受影响范围
```

- 夹具 `ClouderyApi.Tests/TestSupport/ClouderyApiFactory.cs` 用**环境变量**注入 Casdoor（`Casdoor__Endpoint` 等）与管理员（`Authorization__Admins__0`），因此不依赖本地 `appsettings.json`（该文件不入库）；`IntegrationTestBase.cs` 每个测试类建库、结束时清理连接池。
- CI（`.github/workflows/dotnet.yml` 与 `deploy.yml`）都起了 `mysql:8.0` 服务容器，并在 Test 之前执行 `SET GLOBAL max_connections=500`（容器默认 151，会被并发测试类打满而报 `Too many connections`）。
## GitHub Actions

| 工作流 | 触发 | 作用 |
| ------ | ---- | ---- |
| `.github/workflows/dotnet.yml` | Pull Request → `master` | `dotnet-version: 10.0.x` → `restore` → `build --configuration Release` → `test`，并上传构建产物到 Artifacts |
| `.github/workflows/deploy.yml` | Push → `master`（也可手动触发） | 构建 + 测试 + 发布 → 上传到 1Panel 应用目录 → 重启容器 → 健康检查 |

推送 `master` 即自动部署。服务器容器配置、GitHub Secrets 清单、回滚方式与常见坑见 **[DEPLOY.md](DEPLOY.md)**。

## 安全注意事项

- 所有需授权的写操作依赖 Cookie 会话与 CSRF 校验；请确保生产环境走 HTTPS（Cookie 为 `Secure`）。
- 敏感数据（白名单/周目/申请/成员/内部试卷）的写操作由 `AdminOnlyAttribute`（基于 policy 的 `AdminOnlyAuthorization`，配置 `Authorization:Admins`）限管理员；写接口绑定输入 DTO（如 `ExamPaperInput`）并由服务端生成主键与时间戳，客户端多余字段不会影响实体（防越权与 over-posting）；请求模型校验由 `[ApiController]` 自动完成（400 ValidationProblemDetails）。
- 用户内容（MHOP 帖子 / 回复）由前端渲染边界防御 XSS（markdown 经 DOMPurify 净化、纯文本经 Vue `{{}}` 转义），服务端保持原样返回；会话 Cookie 已设 `HttpOnly=true`。
- 点赞基于 `mhop_likes` 去重表实现幂等切换，避免并发计数不一致。
- 内置按 IP 的固定窗口限流（每 60 秒 300 次），缓解接口被刷与爆破。
- 已按代码审查移除公开的骂人接口 `/misc/maren`。
- 长链跳转接口严格校验目标为 http/https 绝对地址，防止 `javascript:`、`data:` 等危险协议。
- 切勿将 `appsettings.json`（含真实密钥）提交至版本库。

## 许可证

本项目基于 **GNU Affero General Public License v3.0（AGPL-3.0）** 开源。

AGPL-3.0 是基于网络服务的强 Copyleft 协议：你可以自由使用、修改、再分发本软件；但若你**修改后部署到服务器上并向其他用户提供服务**（通过网络远程交互），则必须将修改后的源码以同样协议向所有用户开放获取。详细条款见 [LICENSE](LICENSE)。

完整协议文本可在 <https://www.gnu.org/licenses/agpl-3.0.html> 获取。

---

*ClouderyApi — 为 Cloudery 生态（云术 / 竹像素 / MHOP）提供统一后端能力的开源 Web API 服务。*
