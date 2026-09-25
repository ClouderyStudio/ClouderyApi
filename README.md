# ClouderyApi

ClouderyApi 是驱动 Cloudery 生态各站点后端的 ASP.NET Core Web API 服务（目标框架 **.NET 10**）。
它为多个关联子项目提供统一接口：云术（Cloudery）团队站、栖所（Qisoul）情绪记录社区、竹像素（Zhuxs）白名单与申请系统、SurvivalCraft 服务器接口、公益心理辅助平台 **MHOP**（匿名倾诉论坛 / AI 陪伴 / 心理量表评估 / 管理后台），以及若干通用工具接口。

## 功能概览

| 模块 | 路由前缀 | 说明 |
| ---- | -------- | ---- |
| 身份认证 | `/identity/auth` | 基于 **Casdoor** OAuth2 的登录 / 回调 / 登出 / 当前用户查询，Cookie 会话 + CSRF 防护；另有 `GET /config` 供第三方站点取登录元数据 |
| 团队成员 | `/cloudery/members` | 团队 / 组织成员信息（姓名、职位、简介、社交链接）增删改查 |
| 内部试卷 | `/exam/ExamPapers` | 内部测试试卷（心理学项目）整卷 JSON 存于 `ExamPapers` 表；公开读（**不含答案/解析**）+ `POST /{id}/grade` 服务端判分，写操作需管理员；`/exam/ExamPapers/{id}/full`（管理员）读取含答案全量 |
| 情绪记录 | `/qisoul/mood` | 情绪打卡（类型、标签、强度 1-5、情绪日记、备注、标签） |
| 帖子 | `/qisoul/post` | 社区文章（分类、图标、点赞、评论数、编辑） |
| 评论 | `/qisoul/comment` | 帖子评论，支持嵌套回复 |
| 便签 | `/qisoul/sticky` | 便签（内容、图标、颜色、点赞） |
| 统计 | `/qisoul/stats` | 情绪数据分析：累计天数、连续天数、今日情绪、趋势与分布 |
| 白名单 | `/zhuxs/whitelists` | 竹像素白名单（邀请码）管理 |
| 申请 | `/zhuxs/applications` | 入服申请审核（是否通过、申请时间、FAQ 问答） |
| 周目 | `/zhuxs/terms` | 周目信息（名称、起止时间、版本、模组数、人数、模组文件） |
| 服务器 | `/sc/server` | SurvivalCraft 服务器接口（转发 / 查询） |
| 长链 | `/misc/longlink` | 将普通链接编码为 IPv6.arpa 长链，解码并安全跳转（仅允许 http/https） |
| MHOP 公共 | `/mhop` | 健康检查、援助热线、在线人数心跳（匿名） |
| MHOP 认证 | `/mhop/auth` | 注册 / 用户名密码登录 / 邮箱验证码登录 / **Casdoor 统一身份登录** / 当前用户 / 资料与手机号绑定（登录后统一签发 **JWT Bearer**） |
| MHOP 论坛 | `/mhop/forum` | 板块、帖子、回复（先审后发）、AI 自动回复、点赞；`/mine/*` 与作者自助编辑 / 撤回审核 / 重新提交 / 删除 |
| MHOP 量表 | `/mhop/assessments` | PHQ-9 / GAD-7 / 自由倾诉：服务端计分 + AI 解读 |
| MHOP 后台 | `/mhop/admin` | 数据看板、帖子巡检、回复审核、AI 回复撤回/恢复、用户管理、AI 日志（需管理员） |
| MHOP 上传 | `/mhop/upload` | 头像 / 帖子图片上传；存储可切换本地磁盘（`/mhop/uploads/*`）或**远端阿里云 OSS** |

## 技术栈

- **ASP.NET Core**（net10.0），控制器 `[ApiController]` 风格 REST API
- **Entity Framework Core**，主要使用 **MySQL** 驱动（`MySql.EntityFrameworkCore`），同时引入 SQL Server 提供程序与迁移
- 三个 `DbContext`：`ClouderyApiContext`（云术 / 竹像素域，含 JSON 列转换）、`QisoulDbContext`（栖所域，含索引、默认值、导航属性配置）、`MhopDbContext`（MHOP 域，表名统一加 `mhop_` 前缀以隔离，含点赞唯一索引与级联删除）
- **Casdoor** OAuth2 认证（`Casdoor.AspNetCore` + `Casdoor.Client`），Cookie 会话，会话有效期 7 天且支持滚动续期
- **MHOP 认证**：手写 HS256 JWT（`Authorization: Bearer`）+ PBKDF2-SHA256 密码哈希，兼容原有协议；并提供 **Casdoor 统一身份认证（OAuth2 授权码 / OIDC）** 登录，成功后同样签发 MHOP JWT
- **SixLabors.ImageSharp** 处理上传图片：头像方形裁剪、帖子图缩放、统一 WebP 编码（对应 Python 版的 Pillow）
- 自定义 **CSRF 防护**中间件：对 POST/PUT/PATCH/DELETE 请求校验 Origin 头是否在 CORS 白名单内
- **Swagger / OpenAPI**（开发环境启用）
- **Costura.Fody** 将依赖程序集嵌入，便于单文件分发
- GitHub Actions 自动化构建（`.github/workflows/dotnet.yml`）

## 目录结构

```
ClouderyApi/
├── Program.cs                     # 入口：服务注册、认证、CORS、CSRF 中间件、Swagger
├── ClouderyApi.csproj             # 项目文件与 NuGet 依赖
├── appsettings.example.json       # 配置示例（提交到仓库）
├── appsettings.json               # 实际配置（含密钥，已被 .gitignore 忽略，不入库）
├── ClouderyApi.http               # HTTP 调试脚本（VS 使用）
├── Controllers/
│   ├── Auth/AuthController.cs
│   ├── Cloudery/           # Members / ExamPapers
│   ├── Filters/AdminOnlyAttribute.cs   # 管理员角色鉴权过滤器
│   ├── MHOP/                      # 见下方「MHOP 模块」：Common / Auth / Forum / Assessment / Admin / Upload
│   ├── Misc/LongLinkController.cs
│   ├── Qisoul/                    # Mood / Post / Comment / Sticky / Stats
│   ├── SurvivalCraft/ServerController.cs
│   └── Zhuxs/                     # Applications / Terms / Whitelists
├── Data/
│   ├── ClouderyApiContext.cs
│   ├── QisoulDbContext.cs
│   ├── MhopDbContext.cs          # MHOP 域（表名 mhop_ 前缀）
│   └── MhopDbContextFactory.cs   # MHOP 设计时工厂（dotnet ef）
├── Models/
│   ├── Cloudery/                 # Member（实体）+ MemberDto、ExamPaper（含嵌套类型）
│   ├── Mhop/                     # MHOP 实体（MhopUser/Post/Reply/Like/Assessment/AiLog）+ DTOs
│   ├── Qisoul/                   # 实体 + DTOs + UserLike（点赞去重表）
│   └── Zhuxs/                    # 实体 + DTOs
├── Migrations/                    # QisoulDbContext（SQL Server）迁移；Migrations/ClouderyApi/ 为 ClouderyApiContext（MySQL，含 ExamPapers 迁移）；Migrations/Mhop/ 为 MhopDbContext（MySQL）
└── Properties/launchSettings.json # 开发启动配置（端口 5171 / 7288）
```

## 快速开始

### 环境要求

- [.NET SDK 10.0](https://dotnet.microsoft.com/download)
- MySQL（`MySql.EntityFrameworkCore` 驱动）或 SQL Server
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
| `Mhop` | MHOP 模块：`Jwt`（密钥 / 有效期）、`Llm`（OpenAI 兼容大模型，留空走本地兜底）、`Smtp`（邮箱验证码，`Host` 留空为开发模式）、`Casdoor`（统一身份登录开关与回调地址）、`LekeHotline`、`UploadDir`、`AutoMigrate` / `Seed` |

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

两个 `DbContext` 各自维护迁移：`QisoulDbContext`（SQL Server）在 `Migrations/`，`ClouderyApiContext`（MySQL）在 `Migrations/ClouderyApi/`。生成并应用迁移：

```bash
dotnet ef migrations add <Name> --context QisoulDbContext
dotnet ef database update --context QisoulDbContext

dotnet ef migrations add <Name> --context ClouderyApiContext
dotnet ef database update --context ClouderyApiContext

dotnet ef migrations add <Name> --context MhopDbContext --output-dir Migrations/Mhop
dotnet ef database update --context MhopDbContext
```

> 首次部署 MHOP 模块前执行 `dotnet ef database update --context MhopDbContext` 创建 `mhop_*` 表。开发环境下 `Mhop:AutoMigrate` 默认为 `true`，启动时自动迁移；生产环境建议保持 `false` 手动执行。

> 内部试卷表迁移 `AddExamPapers` 仅新增 `ExamPapers` 表（整卷 JSON 存单列，兼容既有 schema）。存在多个 `DbContext` 时，`dotnet ef` 命令需显式指定 `--context`。

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
- **序列化**：MHOP 控制器统一通过 `MhopJson.Options` 输出**蛇形字段名**与 **UTC（带 Z）时间**；
  请求体用 `[JsonPropertyName]` 显式绑定蛇形键名。错误统一为 `{ "detail": "..." }`（与 FastAPI 一致）。
- **AI**：`Services/Mhop/MhopAiService.cs` 调用任意 OpenAI 兼容 `/chat/completions`；
  未配置或调用失败时降级为内置共情式规则回复；任何引擎下检测到危机信号都会强制前置援助热线。
- **后台任务**：发帖后通过独立 DI 作用域异步生成 AI 回复并写入 `mhop_ai_logs`（关联 `reply_id`，可在后台撤回 / 恢复）。
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

### 数据表（`mhop_` 前缀，与栖所等已有域隔离）

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
| `PUT /mhop/forum/posts/{id}`、`PUT /mhop/forum/replies/{id}` | 编辑本人内容，仅待审核 / 草稿可改；帖子编辑后会丢弃并重新生成 AI 回复 |
| `POST /mhop/forum/posts/{id}/withdraw`、`.../replies/{id}/withdraw` | 取消审核：待审核 → 草稿 |
| `POST /mhop/forum/posts/{id}/submit`、`.../replies/{id}/submit` | 重新提交审核：草稿 → 待审核 |
| `DELETE /mhop/forum/posts/{id}`、`DELETE /mhop/forum/replies/{id}` | 删除本人内容（任意状态）；删帖会一并清理其回复与点赞 |

- 仅作者本人可操作，他人调用一律 404；草稿不进入公开列表、不计入后台统计，也不能被他人回复或点赞。
- 对已通过 / 已驳回内容调用编辑接口会返回 400「已通过审核的内容不可修改，仅可删除」。

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

### 与 Python 版的实现说明

- **图片上传**：与 Python 版行为一致——校验 ≤5MB 与 MIME（JPG/PNG/WebP/GIF），头像居中裁剪为 200×200 方图、
  帖子图宽度超过 800 时等比缩放，统一以 **WebP（质量 82）** 保存。差异有两点：实现库 Pillow →
  **SixLabors.ImageSharp**（纯托管，可被 Costura 嵌入）；落盘目标由写死本地目录改为可切换的 `IMhopObjectStorage`
  （本地磁盘或远端阿里云 OSS，见上一节）。
- **在线人数 / 邮箱验证码**：与 Python 版一致为单进程内存实现，多实例部署请替换为 Redis。
- **SMTP**：内置极简 SMTP 客户端，同时支持隐式 SSL（465）与 STARTTLS（587）。

### 默认账号

种子数据（`Mhop:Seed=true`）会创建管理员 `admin / admin123` 与一条引导帖，
首次登录后请立即修改密码。`Mhop:AutoMigrate` 在开发环境默认开启。

## GitHub Actions

`master` 分支创建构建工作流（`.github/workflows/dotnet.yml`）：

- 使用 `dotnet-version: 10.0.x`
- `dotnet restore` → `dotnet build --configuration Release ClouderyApi` → `dotnet test`
- 上传构建产物到 Artifacts

## 安全注意事项

- 所有需授权的写操作依赖 Cookie 会话与 CSRF 校验；请确保生产环境走 HTTPS（Cookie 为 `Secure`）。
- 敏感数据（白名单/周目/申请/成员/内部试卷）的写操作由 `AdminOnlyAttribute` 限管理员（配置 `Authorization:Admins`），主键由服务端生成并校验 `ModelState`（防越权与 over-posting）。
- 用户内容（帖子/评论/心情/便签）由前端渲染边界防御 XSS（markdown 经 DOMPurify 净化、纯文本经 Vue `{{}}` 转义），服务端保持原样返回；会话 Cookie 已设 `HttpOnly=true`。
- 点赞基于 `UserLike` 去重表实现幂等切换，评论数在增删后重新统计，避免并发计数不一致。
- 内置按 IP 的固定窗口限流（每 60 秒 300 次），缓解接口被刷与爆破。
- 已按代码审查移除公开的骂人接口 `/misc/maren`。
- 长链跳转接口严格校验目标为 http/https 绝对地址，防止 `javascript:`、`data:` 等危险协议。
- 切勿将 `appsettings.json`（含真实密钥）提交至版本库。

## 许可证

本项目基于 **GNU Affero General Public License v3.0（AGPL-3.0）** 开源。

AGPL-3.0 是基于网络服务的强 Copyleft 协议：你可以自由使用、修改、再分发本软件；但若你**修改后部署到服务器上并向其他用户提供服务**（通过网络远程交互），则必须将修改后的源码以同样协议向所有用户开放获取。详细条款见 [LICENSE](LICENSE)。

完整协议文本可在 <https://www.gnu.org/licenses/agpl-3.0.html> 获取。

---

*ClouderyApi — 为 Cloudery 生态（云术 / 栖所 / 竹像素 / MHOP）提供统一后端能力的开源 Web API 服务。*
