# ClouderyApi

ClouderyApi 是驱动 Cloudery 生态各站点后端的 ASP.NET Core Web API 服务（目标框架 **.NET 10**）。
它为多个关联子项目提供统一接口：云术（Cloudery）团队站、栖所（Qisoul）情绪记录社区、竹像素（Zhuxs）白名单与申请系统、SurvivalCraft 服务器接口，以及若干通用工具接口。

## 功能概览

| 模块 | 路由前缀 | 说明 |
| ---- | -------- | ---- |
| 身份认证 | `/identity/auth` | 基于 **Casdoor** OAuth2 的登录 / 回调 / 登出 / 当前用户查询，Cookie 会话 + CSRF 防护；另有 `GET /config` 供第三方站点取登录元数据 |
| 团队成员 | `/cloudery/members` | 团队 / 组织成员信息（姓名、职位、简介、社交链接）增删改查 |
| 内部试卷 | `/exam/ExamPapers` | 内部测试试卷（心理学项目）整卷 JSON 存于 `ExamPapers` 表；公开读（**不含答案/解析**）+ `POST /{id}/grade` 服务端判分，写操作需管理员 |
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

## 技术栈

- **ASP.NET Core**（net10.0），控制器 `[ApiController]` 风格 REST API
- **Entity Framework Core**，主要使用 **MySQL** 驱动（`MySql.EntityFrameworkCore`），同时引入 SQL Server 提供程序与迁移
- 两个 `DbContext`：`ClouderyApiContext`（云术 / 竹像素域，含 JSON 列转换）、`QisoulDbContext`（栖所域，含索引、默认值、导航属性配置）
- **Casdoor** OAuth2 认证（`Casdoor.AspNetCore` + `Casdoor.Client`），Cookie 会话，会话有效期 7 天且支持滚动续期
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
│   ├── Misc/LongLinkController.cs
│   ├── Qisoul/                    # Mood / Post / Comment / Sticky / Stats
│   ├── SurvivalCraft/ServerController.cs
│   └── Zhuxs/                     # Applications / Terms / Whitelists
├── Data/
│   ├── ClouderyApiContext.cs
│   └── QisoulDbContext.cs
├── Utilities/SecurityHelper.cs   # 输出 HTML 编码（防存储型 XSS）
├── Models/
│   ├── Cloudery/                 # Member（实体）+ MemberDto、ExamPaper（含嵌套类型）
│   ├── Qisoul/                   # 实体 + DTOs + UserLike（点赞去重表）
│   └── Zhuxs/                    # 实体 + DTOs
├── Migrations/                    # QisoulDbContext（SQL Server）迁移；Migrations/ClouderyApi/ 为 ClouderyApiContext（MySQL，含 ExamPapers 迁移）
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
```

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

*ClouderyApi — 为 Cloudery 生态（云术 / 栖所 / 竹像素）提供统一后端能力的开源 Web API 服务。*
