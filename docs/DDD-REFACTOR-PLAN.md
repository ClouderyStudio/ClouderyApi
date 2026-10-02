# ClouderyApi DDD 改造方案（评估 + 分阶段实施计划）

> 状态：已批准实施。Stage 0（安全网测试）已完成并提交（ac6c00c）；下方保留原方案并追加实施记录。
> 项目：ClouderyApi.sln / ClouderyApi/ClouderyApi.csproj（net10.0，MySQL，Oracle MySql.EntityFrameworkCore 10.0.9）
> 目标：参考 DDD 架构理念，改善可维护性，**同时保持对外 HTTP 路由与 JSON 契约完全不变**。

---

## 0. 摘要（TL;DR）

- **现状**：单程序集、按类型分层（Controllers / Models / Services / Data）；三个 DbContext 近似限界上下文（ClouderyApiContext、IdentityDbContext、MhopDbContext）。领域模型是贫血 POCO，业务规则散落在控制器与少数服务中。
- **主要症状**：胖控制器（MhopForumController 797 行、MhopAdminController 533 行）；控制器直连 DbContext（MHOP 内共 118 处 _db. 引用）；状态机 / 校验 / 映射重复；用裸字符串与整数代替值对象；无领域事件；无测试项目。
- **建议路线**：以 **MHOP 为先导**（项目里已存在 MhopBottleService 这一“正确形态”的参照实现），分 5 个阶段推进：安全网测试 → 值对象与充血模型 → 应用层抽取 → 目录按限界上下文重组 → 领域事件与事务边界；横切治理并行处理。
- **硬约束**：路由、响应 JSON（snake_case、错误体 {detail}）、鉴权语义与关键行为逐字不变（见第 6 节红线）。
- **非目标**：不拆微服务 / 多程序集（本方案先做模块化单体）；不改数据库表结构（除非明确标注为可选项）；不改前端。
- **粗估工作量**（单人粒度，供排期参考）：Stage 0 约 2–3 天；Stage 1 约 2–4 天；Stage 2 约 5–8 天；Stage 3 约 2–3 天；Stage 4 约 3–5 天；Stage 5 穿插进行。

---

## 1. 背景与目标

### 1.1 为什么要做
- 当前业务规则与 HTTP、EF 持久化混在一起，任何规则调整都要同时改控制器和 SQL 查询，回归成本高。
- 没有测试，重构风险被放大；也没有明确的模块边界，Cloudery 与 Zhuxs 共用一个 DbContext，跨域耦合容易失控。
- MHOP 是从 Python FastAPI 迁移过来的重灾区：论坛、漂流瓶、审核、权限几套规则交织，控制器已接近 800 行。

### 1.2 设计原则（本次改造的取舍）
- **行为保持优先**：先立安全网，再做结构改造；每一步都能 dotnet build + 测试通过。
- **渐进式**：不搞一次性大爆炸，按聚合 / 控制器 / 阶段分批提交，随时可停。
- **按收益排序**：先解决“规则无处安放”的贫血模型和胖控制器，再谈目录重组。
- **不引入重量级依赖**：领域事件可选 MediatR，但默认用轻量进程内派发；值对象用 EF value converter 映射回原列，不产生迁移。

### 1.3 非目标
- 不拆分为多个可独立部署的服务。
- 不改数据库表名、列类型、主键、索引（本方案默认零迁移）。
- 不重写认证体系（Casdoor Cookie + MHOP JWT 并存保持不变）。

---

## 2. 现状评估

### 2.1 架构全景
- 115 个 .cs 文件，约 12,200 行；单项目（ClouderyApi.sln:6），无 analyzer / .editorconfig，Nullable 已开启。（Stage 0 已新增测试项目 ClouderyApi.Tests。）
- 目录按类型分层：Controllers/、Services/、Models/、Data/、Migrations/。命名空间二级再按领域分（Cloudery、Zhuxs、Mhop、Identity），即“按层分包、领域散落”。
- 对外有四类鉴权：Casdoor Cookie（identity/）、AdminOnly 过滤器（cloudery/、zhuxs/、exam/ 部分）、MHOP 自有 JWT + MhopPerm 特性（mhop/）、SurvivalCraft 静态 Token。

### 2.2 请求管线（Program.cs:143-274）
| 顺序 | 中间件 | 位置 |
|---|---|---|
| 1 | 内存固定窗口限流 300 req/60s（早于 CORS） | Program.cs:187-207 |
| 2 | CORS AllowAllOrigins | Program.cs:209 |
| 3 | CSRF Origin 校验（仅非 Development） | Program.cs:215-240 |
| 4 | UseHttpsRedirection | Program.cs:244 |
| 5 | /mhop/uploads 静态托管 | Program.cs:248-258 |
| 6 | UseAuthentication / UseAuthorization | Program.cs:261-263 |
| 7 | MapControllers | Program.cs:265 |

- 全局异常过滤器 MhopApiExceptionFilter 在 Program.cs:19 注册（MHOP 专用类型被全局挂载，属边界泄漏）。
- 无全局 [Authorize]，鉴权为逐控制器显式声明，容易遗漏。

### 2.3 三个 DbContext 与迁移
| 上下文 | 位置 | DbSet 数 | 设计时工厂 | 迁移目录 |
|---|---|---|---|---|
| ClouderyApiContext | Data/ClouderyApiContext.cs:8 | 6 | **无** | Migrations/ClouderyApi/ |
| IdentityDbContext | Data/IdentityDbContext.cs:10 | 1 | 有 | Migrations/Identity/ |
| MhopDbContext | Data/MhopDbContext.cs:10 | 8 | 有 | Migrations/Mhop/ |

- 三者共用同一个数据库连接串（Program.cs:27/31/57），仅靠表名前缀（mhop_）区分，且**共享 __EFMigrationsHistory**（未配置 MigrationsHistoryTable）。
- ClouderyApiContext 同时承载 Cloudery 成员 / 试卷 / 成绩与 Zhuxs 白名单 / 条款 / 申请（Data/ClouderyApiContext.cs:12-17），被两族控制器注入。
- ClouderyApiContext 没有设计时工厂，生成其迁移必须启动 Web 主机（README.md:121-132）。
- Migrations/ClouderyApi 与 Migrations/Mhop 中存在 VS 脚手架遗留的误导性命名：20260906124754_mssql.local_migration_995、20261001035824_mssql.local_migration_832（实为 MySQL）。

### 2.4 DI 生命周期与工作单元
- 三个 DbContext 均 scoped；DB 相关的 singleton 服务（MhopAiService、MhopContentReviewService、MhopBottleTimeoutService）都通过 IServiceScopeFactory 自行开 scope，**无 captive dependency**。
- 但**没有显式 UoW / Repository**；scoped DbContext 存活整个请求，各服务自行 SaveChangesAsync（全库 29 处裸 SaveChanges）。
- 全库**唯一一处显式事务**在 MhopBottleService.cs:115（捞瓶的条件 UPDATE），其余多写流程依赖单次 SaveChanges 的隐式事务。

### 2.5 已有的良好实践（改造时保留、复用）
- **漂流瓶上下文就是目标形态**：MhopBottleController（8 个端点全部委托）→ MhopBottleService（规则集中）→ MhopBottleMapper（映射集中）。
- IMhopObjectStorage（本地 / 阿里云 OSS 两实现）、MhopOptions（单一配置根）。
- 授权集中：MhopAdminAttribute / MhopPerm / MhopSuper + MhopCurrentUserAccessor（MhopCurrentUserAccessor.cs:41-76），权限码在 MhopAdminPermissions。
- 内容筛查集中：MhopContentPolicy（确定性规则）+ MhopAiService（LLM 降级），顺序明确。
- 序列化集中：MhopJson.cs:14-23；错误体统一 {detail}。
- ExamResultService 把“按 ClientKey 幂等同步 + 配额 + UTC 归一 + payload 大小守卫”封装得较完整（ExamResultService.cs:19-25、:202-217）。
- ExamPaperView 真正隐藏答案（ExamPaper.cs:40-59 + ExamPapersController.cs:17-32）。
- 共享内核真实复用：ILlmClient / LlmOptions、CrisisSupport 被 MHOP 与 Cloudery 共同使用。

---

## 3. DDD 差距清单（按证据）

### 3.1 战略：限界上下文与模块边界
- **一个 DbContext 混装两个子域**：ClouderyApiContext.cs:12-17 同时声明 ZhuxsWhitelists / ZhuxsTerms / ZhuxsApplications / ClouderyMembers / ExamPapers / ExamResults，被 Controllers/Cloudery/* 与 Controllers/Zhuxs/* 共同注入 → 无模块隔离。
- 单程序集内任何模块都能直接注入别人的 DbContext（三个上下文在同一 DI 容器，Program.cs:25-57）。
- MHOP 专用异常过滤器全局注册（Program.cs:19 + MhopApiExceptionFilter.cs:10）。
- AdminOnlyAttribute 把 IdP（Casdoor）概念渗入所有模块的管理员判定（AdminOnlyAttribute.cs:25-30）。
- 计数器：MHOP 控制器中 _db. 直接引用共 118 处（Forum 46、Admin 50、Auth 13、Casdoor 6、Assessment 3）。

### 3.2 战术：贫血模型与聚合缺失
- MhopPost / MhopReply 是纯字段袋（Models/Mhop/MhopPost.cs:7-53、MhopReply.cs:7-54），Status 是裸 int，没有任何状态转换方法。所有转换在外部开写：MhopForumController.cs:497-529、:614；MhopAdminController.cs:111-133、:205-221；MhopContentReviewService.cs:59-149。
- MhopUser（Models/Mhop/MhopUser.cs:10-64）不承载角色 / 状态规则；权限解析在 MhopAdminPermissions.cs:41-65，角色变更在 MhopAdminController.cs:350-401。
- MhopBottle 已在实体文件里定义常量（MhopBottle.cs:7-23、:26-30），但派生转换仍在 MhopBottleService.cs:425-486。
- Member / Whitelist / Application / Term / ExamPaper / ExamResult 同样全是属性袋，规则在控制器；试卷评分算法整段写在 ExamPapersController.cs:59-142。

### 3.3 原始类型迷恋（值对象缺失）
- 角色 / 状态字符串字面量："user"/"admin"/"superadmin"/"active"/"disabled" 散落在 MhopAuthController.cs:66/173/204、MhopAdminController.cs:307-401、MhopCurrentUserAccessor.cs:44/52/90、MhopAdminPermissions.cs:29/31、MhopSeeder.cs:30-56。
- 状态整数裸用：MhopForumController.cs:152/203/243、MhopBottleAdminController.cs:206(2)/:215(1)、MhopContentReviewService.cs:263、MhopBottleService.cs:137（原生 SQL SET Status = 2）。
- 点赞对象 "post"/"reply" 字面量：MhopForumController.cs:252-291、MhopContentService.cs:32/65、MhopAiService.cs:321/351。
- 板块 slug、图片 JSON 字符串（Images）、AI 标记（suspect/violation/unavailable/approved）均以原始类型流转。

### 3.4 状态机与常量重复
- 内容状态常量重复定义：MhopForumController.cs:312-315 与 Services/Mhop/MhopContentStatus.cs:12-15（服务文件自己注释说明“与控制器常量保持一致”）。
- Draft→Pending 的 submit 实现了两遍（帖子 MhopForumController.cs:512-529、回复 :598-615），且清空 ReviewNote/AiFlag/AiReviewNote/AiReviewedAt 的逻辑逐字重复。
- Pending→Draft 的 withdraw 同样两遍（:498-509、:584-595）。
- 审核通过 / 驳回的状态数字直接硬编码（MhopAdminController.cs:118-133、:211-221）。
- 授权规则双重执行：[MhopPerm(Bottles)] 与 RequirePermAsync 在每个漂流瓶管理端点上各查一次（MhopBottleAdminController.cs:22-23、:118-119 等）。

### 3.5 应用层缺失 / 胖控制器
- MhopForumController（797 行）把 HTTP、校验、EF 查询、状态流转、映射全塞在一起；mapping 辅助占 MhopForumController.cs:642-795。
- MhopAdminController（533 行）包含审核、用户与权限、统计、AI 日志。
- 校验重复：帖子 1–2000 字在创建 :181-186 与编辑 :463-470 各写一遍；回复 1–1000 字在 :215-220 与 :546-553 各写一遍；敏感词重筛查在 :556-565 还有第三份内联实现。
- Cloudery / Zhuxs 全无服务层，控制器直接落库；ExamPapersController 的评分是领域逻辑却住在控制器。

### 3.6 仓储与事务
- 无 IRepository / IUnitOfWork（全库 grep 无命中）。
- 多写删除（MhopContentService 级联）依赖“先 DB 后存储”的非原子顺序（MhopContentService.cs:7-11 注释承认）。
- 除漂流瓶捞取外，其余多实体写入无显式事务。

### 3.7 领域事件缺失
- 无 MediatR / IDomainEvent；副作用以命令式内联或 Task.Run fire-and-forget 触发：MhopForumController.cs:206/246/489、MhopBottleAdminController.cs:154/191、MhopContentReviewService.cs:45-55、MhopBottleTimeoutService.cs:16-43（轮询兜底）。
- 后果：无 outbox；进程重启会丢重试队列（靠 MhopBottleTimeoutService.cs:32-34 的 requeue 部分缓解）。

### 3.8 输出契约 / DTO
- 5 个控制器直接返回 EF 实体：MembersController（Member）、WhitelistsController（Whitelist，即邀请码）、ApplicationsController（Application）、TermsController（Term）、ExamPapersController（管理端 ExamPaper 含答案）。
- DTO 与实体手工互转散落各处，无统一 mapper；ExamPaper 的 POST/PUT 直接绑定实体，存在 over-posting（ExamPapersController.cs:147/162）。

### 3.9 工程化与横切
- ~~无测试；CI 的 test 步骤是空转~~ —— Stage 0 已修复：两个 workflow 增加 mysql:8.0 service、构建 ClouderyApi.sln、注入 CLOUDERY_TEST_MYSQL，dotnet test 真正运行 43 个用例。
- 启动期直接 Database.Migrate() + Seed（Program.cs:156-183，Seed 默认 true，会创建 admin/admin123 等）；失败仅记 warning。
- 配置直读：AdminOnlyAttribute.cs:26-30、AuthController.cs:34-43、ServerController.cs:13-19、MhopCasdoorService.cs:30-40。
- 统一 400 契约缺失：DTO DataAnnotations 让框架返回 ValidationProblemDetails，与模块裸 {success,message} 不一致；且 [ApiController] 自动校验使控制器里的 ModelState 检查成为死代码（MembersController.cs:39/63 等）。
- 死并发处理：多处 catch DbUpdateConcurrencyException，但无任何 concurrency token（MembersController.cs:51-55 等）。
- 共享 __EFMigrationsHistory；无限流分布式化；两个鉴权体系无统一 policy。

---

## 4. 目标架构

### 4.1 模块化单体目录结构（按限界上下文聚合）
    ClouderyApi/
      Modules/
        Mhop/
          Domain/
            Entities/        (MhopPost, MhopReply, MhopLike, MhopUser, MhopBottle, MhopBottleMessage, MhopAssessment, MhopAiLog)
            ValueObjects/    (ContentStatus, ContentText, BoardSlug, ImageRefs, LikeTargetType, AiFlag, MhopRole, PermissionSet, BottleStatus, BottleEndReason)
            Services/        (ContentScreeningPolicy, AssessmentScoring, BottleAllocation)
            Events/          (ContentPublished, ContentRejected, BottlePicked, BottleEnded, ...)
          Application/
            Forum/ Admin/ Bottle/ Auth/ Assessment/ Casdoor/   (用例服务)
            Abstractions/    (IMhopObjectStorage, IAiReviewQueue, IClock, 仓储接口)
            Mapping/         (实体 -> DTO)
            Dtos/            (原 Models/Mhop/DTOs)
          Infrastructure/
            Persistence/     (MhopDbContext, EF 配置, 仓储实现)
            Storage/ Ai/ Email/ Security/ Background/
          Api/
            Controllers/ Filters/ (MhopControllerBase, MhopJson, MhopApiExceptionFilter)
        Cloudery/
          Domain/ (Member, ExamPaper, ExamResult, ExamScoring 领域服务)
          Application/ (MembersAppService, ExamPaperAppService, ExamResultAppService, ResultAnalysisAppService)
          Infrastructure/ Api/
        Zhuxs/
          Domain/ (Whitelist, Application, Term)  Application/ Api/
        Identity/
          Domain/ (User)  Application/ (AuthService, UserSyncService)  Infrastructure/ (IdentityDbContext)  Api/
      Shared/
        Ai/ (ILlmClient, LlmOptions, CrisisSupport)
        Filters/ (AdminOnly, IpRateLimit)
      Migrations/ (保持现状，或迁到各模块 Infrastructure 下)

### 4.2 依赖方向（架构测试可强制）
- Api → Application → Domain；Infrastructure → Domain（实现 Application 声明的端口）；Domain 不依赖 EF / ASP.NET / 任何模块。
- 模块之间只允许通过 Application 层接口 / 共享内核交互，禁止直接引用对方 Infrastructure 或 DbContext。

### 4.3 聚合与值对象设计（MHOP 示例）
- **MhopPost（聚合根）**：UpdateContent(ContentText, BoardSlug, isAnonymous, ImageRefs) / Withdraw() / SubmitForReview() / Publish() / Reject(note) / ApplyAiReview(flag, note, at) / ClearReviewState()；只读判定 CanEdit / CanWithdraw / CanSubmit / IsVisible。状态转换只允许 Pending↔Draft、Pending→Published/Rejected，非法转换抛领域异常。
- **MhopReply（聚合根）**：同上 + Recall(reason) / Restore()。
- **MhopUser（聚合根）**：Role(MhopRole)、PermissionSet、Status 值对象；Promote() / Demote() / PromoteSuper() / DemoteSuper() / SetPermissions(set) / Disable() / Enable() / SetBadge(text)；把“最后一个 active superadmin 不可禁用 / 降级”“仅 admin 可被降级”“不可自我停用”等守卫收进实体（现散在 MhopAdminController.cs:307-466）。
- **MhopBottle（聚合根）**：Throw() / Approve() / Pick(userId) / End(reason, by) / Report(reason, by) / MarkRemoved() / Restore() / AppendMessage() / UnreadCountFor(userId)；ReportedBy 用值对象封装去重与 JSON。
- **领域服务**：AssessmentScoring（从 MhopAssessmentController.cs:39-115 抽出）、ContentScreeningPolicy（MhopContentPolicy 收编内联的敏感词重筛查）。

### 4.4 与现有迁移的兼容策略（关键）
- **值对象用 EF HasConversion 映射回原列**：Role / Status / Permissions / Images / AiFlag 在数据库里仍是原字符串 / 整数，**不产生任何迁移**。
- 若在 Stage 3 移动 DbContext 的命名空间，必须同步更新 Migrations/**/*.Designer.cs 与 *ModelSnapshot.cs 中的 [DbContext(typeof(...))] 引用；也可以选择让 DbContext 暂时留在 ClouderyApi.Data 命名空间（推荐，零风险），只移动领域 / 应用代码。
- 迁移目录可维持现状；若要下移到模块目录，只需改 output-dir 与迁移文件的命名空间，DB 无需变更。

---

## 5. 分阶段实施计划

| 阶段 | 主题 | 主要收益 | 风险 | EF 迁移 |
|---|---|---|---|---|
| Stage 0 | 安全网（测试） | 后续重构可回归 | 中 | 无 |
| Stage 1 | 值对象 + 充血模型 | 规则收口、消除重复常量 | 中 | 无 | ✅ 已完成（MHOP） |
| Stage 2 | 应用层抽取、控制器瘦身 | 消除 118 处 _db.、去重 | 高 | 无 |
| Stage 3 | 目录按限界上下文重组 | 模块边界清晰 | 中 | 无（除非移动 DbContext 命名空间，也不需要） |
| Stage 4 | 领域事件 + 事务边界 | 解耦副作用、保证一致性 | 中高 | 可选 outbox |
| Stage 5 | 横切 / 工程化 | 可观测、可测试、修缺陷 | 低中 | 拆上下文时才需要 |

### Stage 0 — 建立安全网 ✅ 已完成（提交 ac6c00c）
- **实际做法**：新增 ClouderyApi.Tests（xUnit + Microsoft.AspNetCore.Mvc.Testing）；**未用 Testcontainers**，改为 `CLOUDERY_TEST_MYSQL` 指向真实 MySQL（本地用便携 mysqld，CI 用 mysql:8.0 service），每个测试类自建/自删一次性库 `cloudery_test_<16位>`（TestSupport/MySqlTestServer.cs、IntegrationTestBase.cs）。
- **已覆盖契约（43 用例全绿，约 47s）**：MHOP 鉴权（注册/登录/错误体 {detail}）、后台权限阶梯（401/403）、论坛读写与 snake_case 序列化、Zhuxs 匿名读与 camelCase、身份 Cookie 鉴权（TestSupport/AuthCookie.cs 用真实 Cookies 方案的 TicketDataFormat 造票据）。
- **未能自动化**：漂流瓶 throw→pick→messages→end/report 依赖 LLM/存储，暂以读契约覆盖（后续可加 stub）。
- **踩坑记录**：临时诊断文件 DiagTests.cs 用于 dump 认证方案与响应，验证后已删除；不要在没有把握时重现。

### Stage 1 — 值对象与充血模型 ✅ 已完成（MHOP 模块，6 次提交）
- **本阶段提交**：d7c1fc8 内容聚合 / b1bff0f 漂流瓶聚合 / 57f23eb 用户聚合 / 79a2388 点赞目标类型 / bb237f6 心理评估计分 / 096e1da 业务异常统一（均本地提交、未推送）。
- **目标**：把规则收回模型，消除原始类型与重复常量。
- **进展（内容聚合已完成）**：
  - `ContentStatus`（ClouderyApi/Models/Mhop/ContentStatus.cs）成为唯一来源：删除了 MhopForumController.cs 的 4 个私有常量、Services/Mhop/MhopContentStatus.cs（原文件已删除），并把 forum / admin / MhopContentReviewService / MhopAiService / MhopSeeder 里的 `0/1/2/3` 字面量全部换成常量（漂流瓶消息状态 1/2 已由 MhopBottleMessageStatus 接管）。
  - 新增值对象（ClouderyApi/Models/Mhop/ContentValueObjects.cs）：`ContentText`（trim、非空、帖子 1–2000 / 回复 1–1000，消息文案 `内容不能为空` / `回复内容不能超过 1000 字`）、`BoardSlug`（校验 `MhopBoards.Slugs`，**不 trim** 以保持历史行为）、`ContentScreening`（危机信号 + 敏感词，由边界层用 MhopModeration 计算后传入，领域层不反向依赖服务层）、`ContentRules.TruncateReviewNote`（255 列宽）。
  - `MhopBoards`、`MhopImageRefs` 从 Services/Mhop 移到 Models/Mhop（领域词汇，依赖方向改为 Services → Models）；`MhopImageRefs` 增加 `Normalize`（丢弃空白、≤9 张）与 `Serialize`。
  - `MhopPost` / `MhopReply` 充血：`NewAuthorPost` / `NewAuthorReply` 工厂、`ApplyAuthorEdit`（返回不再被引用的图片地址）、`Withdraw` / `SubmitForReview` / `Publish` / `Reject` / `RejectBySensitiveWords` / `ClearReviewState` / `ClearAiReviewState`、`Recall` / `Restore`、`AddView`，以及 `IsEditable` / `CanWithdraw` / `CanSubmit`；非法转换抛 `DomainRuleException`（ClouderyApi/Models/DomainRuleException.cs），由 MhopApiExceptionFilter 统一映射为 400 `{detail}`。
  - 错误文案与校验顺序逐字保留：帖子编辑为「状态 → 正文 → 板块」、回复编辑为「状态 → 正文」，因此 `ApplyAuthorEdit` 收原始字符串、在方法内部构造值对象；回复编辑**不清**草稿的人工理由、待审核时重新做敏感词复核（历史行为）。
  - AI 自动放行仍走 MhopContentReviewService 的 `ExecuteUpdateAsync` 条件更新（跨作用域 + 内容未变守卫），未改为实体方法，避免改变并发语义。
  - 新增纯单测 ClouderyApi.Tests/DomainContentTests.cs（19 用例，无 HTTP / 无库）。
- **进展（漂流瓶聚合已完成）**：
  - `MhopBottleMessageStatus`（ClouderyApi/Models/Mhop/MhopBottleMessage.cs 内，Visible=1 / Hidden=2）成为瓶消息可见性唯一来源；MhopBottleService / MhopBottleMapper / MhopBottleAdminController / MhopContentReviewService 里的 1/2 字面量全部换成常量。
  - `MhopBottle`（ClouderyApi/Models/Mhop/MhopBottle.cs）充血：静态工厂 `Throw(userId, content, crisis, now)`、`IsParty` / `RoleOf` / `UnreadCountFor`（原服务层静态辅助下沉）、`Pick(pickerId, now)`、`End(byUserId)`、`TouchLastMessage(now)`、`TryReport(userId, reason, now)`（举报去重 + ReportedBy JSON 编解码收进实体，损坏 JSON 视为空）、`MarkRemoved` / `Restore` / `Approve(now)` / `ClearAiFlag` / `SetReviewNote`。
  - `MhopBottleMessage` 充血：`Create(bottleId, senderUserId, content, crisis, now)` / `Hide()` / `Show(now)`（Show 保留首次 AiReviewedAt）。
  - **AI 标记口径收敛**：`MhopModerationOutcome` 从 Services/Mhop 移到 ClouderyApi/Models/Mhop/（领域词汇），新增 `None`（""）与 `Approved`（"approved"），删除 MhopContentReviewService.ApprovedFlag，帖子 / 回复 / 瓶身 / 消息共用同一组常量。
  - 并发语义未动：捞瓶仍是原生 `ORDER BY RAND() LIMIT 1` + 条件 `UPDATE ... WHERE Id=@id AND Status=@drifting`（3 次重试），实体方法只落状态；AI 审核写库仍走 `ExecuteUpdateAsync`（跨作用域 + 内容未变守卫）。
  - 错误文案逐字保留：`该瓶子不在待审核状态`（服务层仍抛 409 MhopApiException，实体 Approve 内另有 DomainRuleException 兜底）、`不支持的处置状态`、`不支持的消息状态`。
  - 新增纯单测 ClouderyApi.Tests/DomainBottleTests.cs（19 用例，无 HTTP / 无库）。
- **进展（用户聚合已完成）**：
  - `MhopUserRole`（user / admin / superadmin）与 `MhopUserStatus`（active / disabled）定义在 ClouderyApi/Models/Mhop/MhopUser.cs 内；MhopUser 充血：`IsStaff` / `IsSuperAdmin` / `IsActive`（get-only 计算属性，不参与映射）、`Promote(PermissionSet)` / `Demote(operatorUserId)` / `PromoteSuper()` / `DemoteSuper(operatorUserId, isLastActiveSuperAdmin)`（返回原保留的模块授权）/ `Disable(operatorUserId, isLastActiveSuperAdmin)` / `Enable()` / `SetBadge(string?)`（trim + 截 64）/ `SetPermissions(PermissionSet)`；非法转换抛 `DomainRuleException`，文案逐字保留。
  - 新增值对象 `PermissionSet`（ClouderyApi/Models/Mhop/PermissionSet.cs）：权限码目录（Dashboard / Review / Users / AiLogs / Bottles + All + Labels）、`From` / `Parse` / `Serialize` / `Contains`；非法码过滤与去重、空 / 非法 JSON → 空集合的旧语义不变。
  - `MhopAdminPermissions`（ClouderyApi/Services/Mhop/MhopAdminPermissions.cs）退化为门面：常量别名 + `IsStaff` / `IsSuper` / `Parse` / `Serialize` / `Has` / `Effective` 委托领域层，59 处调用点签名不变。
  - 角色 / 状态字面量清零：MhopAdminController 的 SetUserStatus / SetUserBadge / SetUserRole / SetPermissions 改为调实体方法（「最后一个可用超管不可停用 / 降级」「不可自我停用 / 自我降级」「仅普通管理员可分配模块权限」守卫进实体）；MhopAuthController / MhopCasdoorController / MhopCurrentUserAccessor / MhopJwtService / MhopSeeder / MhopDbContext / MhopAuthDtos 的 user|admin|superadmin|active|disabled 全部换常量（EF 查询内仍用常量比较，未用计算属性以免破坏翻译）。
  - 授权类 403（`仅超级管理员可操作管理员账号`）仍留在控制器的 GuardStaffTarget：DomainRuleException 一律映射 400，不能改变状态码。
  - `dotnet ef migrations has-pending-model-changes --context MhopDbContext` = 「No changes have been made to the model since the last migration.」。
  - 新增纯单测 ClouderyApi.Tests/DomainUserTests.cs（24 用例，无 HTTP / 无库）。
- **进展（点赞目标类型已完成）**：
  - 新增值对象 `LikeTargetType`（ClouderyApi/Models/Mhop/LikeTargetType.cs）：`readonly record struct`，`PostValue` / `ReplyValue` 常量（复用 MhopContentKind 的词汇）、`Post` / `Reply` 单例、`IsPost` / `IsValid` / `TryParse` / `Parse`（非法值抛 `DomainRuleException("非法点赞对象")`，替换控制器里手写的 400 MhopApiException，JSON 与状态码逐字不变）。
  - `MhopContentKind` 从 Services/Mhop/MhopContentPolicy.cs 移到 ClouderyApi/Models/Mhop/MhopContentKind.cs（领域词汇，依赖方向 Services → Models；两个使用方已 import 该命名空间，无需改动）。
  - 清理 "post" / "reply" 字面量：MhopForumController 的 LikeCountMapAsync / LikedSetAsync 改为收 `LikeTargetType` 参数、ToggleLike / MyLikes 改为 `LikeTargetType.Parse`，MhopContentService（4 处）与 MhopAiService（2 处）的点赞清理查询改用常量。
  - 新增纯单测 ClouderyApi.Tests/DomainLikeTargetTests.cs（13 用例，无 HTTP / 无库）。
- **进展（心理评估计分已完成）**：
  - 新增领域服务 `AssessmentScoring`（ClouderyApi/Models/Mhop/AssessmentScoring.cs）+ 结果记录 `AssessmentScore(Score, Level, LevelCode, Crisis)`：`Evaluate(assessmentType, answers, freeText, crisis)` 负责题库校验（缺题「请完成量表全部题目」、越界「量表作答值非法」）、累计总分、分档，并把量表危机题命中并入危机信号；未知类型「未知的评估类型」、自由文本为空「请先描述你最近的状态与感受」。
  - `MhopScales`（量表目录 / 分档表 / `ToResponse`）从 Services/Mhop/MhopScales.cs 移到 ClouderyApi/Models/Mhop/MhopScales.cs（领域词汇，依赖方向 Services → Models）；MhopAssessmentController.cs:37 / :49 / :63 的调用无需改 using。
  - MhopAssessmentController.Submit 从「内联 40 行计分 + 4 处 400 MhopApiException」瘦身为「取 trim 后的自由文本 → MhopModeration.DetectCrisis → AssessmentScoring.Evaluate → 解构 score / level / levelCode / crisis」；异常类型换成 DomainRuleException，经 MhopApiExceptionFilter 仍映射为 400 `{detail}`，文案与顺序逐字不变。
  - 边界职责划分：关键词危机筛查仍在服务层（MhopModeration），领域层以 `bool crisis` 入参接收后合并量表危机题，领域不反向依赖服务层；自由文本由控制器 trim 后传入（领域不 trim），已由单测钉住。
  - 新增纯单测 ClouderyApi.Tests/DomainAssessmentTests.cs（27 用例，无 HTTP / 无库；含 PHQ-9 分档边界 0/4/5/9/10/14/15/19/20/27、危机题命中、自由文本分支、量表目录契约）。
- **进展（业务异常统一已完成，Stage 1 收尾）**：
  - 纯规则类的 400 业务失败（36 处）由 `MhopApiException(400, "…")` 改为 `DomainRuleException("…")`：MhopUploadController 2、MhopUploadService 6、MhopForumController 4、MhopCasdoorController 2、MhopObjectStorage 1、MhopAuthController 10、MhopAdminController 11；文案逐字保留，仍由 MhopApiExceptionFilter 映射为 400 `{detail}`。
  - 带状态码的业务失败保持 `MhopApiException`（401/403/404/409/422/429/500/502/503，共 65 处），因为领域异常没有状态码语义。
  - `MhopUploadService.cs:82,97` 两处存储守卫 catch 增加 `catch (DomainRuleException) { throw; }`，否则领域异常会落进通用 catch 被改写为「不支持的图片格式」/「图片存储服务暂时不可用」。
  - 全量回归 145 用例全绿，证明状态码与 JSON 未变。
- **关键取舍**：暂不给 `Content` / `Board` / `Images` 加 EF 值转换（原第 5 条），因为 `Contains` / `==` / `GroupBy` / `ExecuteUpdateAsync` 触点太广、易改变可翻译性；先以「构造即校验 + 实体方法」收口规则，列与迁移保持不变。
- **改动清单**：全部完成 ✅（内容 / 漂流瓶 / 用户 / 点赞目标类型 / 心理评估计分 / 业务异常统一；Stage 1 仅覆盖 MHOP 模块，Cloudery、Zhuxs 等模块的同类收口留待 Stage 2 一并处理）。
- **验证**：dotnet build 0 error；dotnet test **145 用例全绿**（Stage 0 契约 43 + DomainContentTests 19 + DomainBottleTests 19 + DomainUserTests 24 + DomainLikeTargetTests 13 + DomainAssessmentTests 27），约 44s。
- **风险**：值对象相等性与 EF 追踪要注意；务必保持 MhopCurrentUserAccessor 返回**被追踪**实体（MhopCurrentUserAccessor.cs:87-88 注释）以满足“改后 SaveChanges”。
- **回滚**：按聚合拆分提交，可单独回退。

### Stage 2 — 应用层抽取、控制器瘦身 🔶 M3 已完成（MHOP）；Cloudery/Zhuxs（M4）进行中
- **目标**：控制器只做 HTTP；用例编排进 Application 层。
- **改动清单（MHOP）**：
  1. ForumAppService 承接 MhopForumController 的读写 / 状态流转 / 点赞 / 我的列表，映射移入 Application/Mhop/Mapping。
  2. AdminAppService 承接 MhopAdminController.cs:42-533 的审核、用户与权限、统计、AI 日志。
  3. MhopBottleService 归位为 BottleAppService（行为逐字保留，控制器不动）。
  4. AuthAppService / AssessmentAppService / CasdoorAppService。
  5. 控制器改为薄适配器（参照 MhopBottleController.cs:20-110）。
- **进展（应用层样板已落地）**：
  - 命名：`Application` 作为根命名空间与 Zhuxs 实体 `ClouderyApi.Models.Zhuxs.Application` 冲突（CS0118："Application"是命名空间，但此处被当做类型，命中 ClouderyApi/Data/ClouderyApiContext.cs 与 Controllers/Zhuxs/ApplicationsController.cs）。因此应用服务统一放 `ClouderyApi/UseCases/<Context>/`、命名空间 `ClouderyApi.UseCases.<Context>`；Stage 3 迁入 `Modules/*/Application` 后不再冲突。
  - `AssessmentAppService`（ClouderyApi/UseCases/Mhop/AssessmentAppService.cs）承接 MhopAssessmentController 的量表目录（`Scales`）、提交用例（`SubmitAsync`：`AssessmentScoring.Evaluate` → MhopAiService 解读 → `save_to_cloud` 可选落库）与我的记录（`MineAsync`）；控制器由 113 行瘦身为 26 行薄适配器（3 个 action 只做模型绑定 + `MhopOk` 包装），路由 / 蛇形字段 / 状态码 / 文案逐字不变。
  - 应用服务在 `Program.cs:87` 注册为 Scoped（`builder.Services.AddScoped<AssessmentAppService>();`）。
  - `ForumAppService`（ClouderyApi/UseCases/Mhop/ForumAppService.cs，552 行）承接论坛 19 个 action 的全部用例编排（板块 / 统计 / 列表 / 详情 / 发帖 / 回复 / 点赞 / 我的内容 / 编辑 / 撤回 / 重提 / 删除）；纯映射（`ParseImages` / `AuthorForPost` / `AuthorForReply` / `ToReplyOut` / `ToPostOut` / `CopyPostFields`）移入 `ClouderyApi/UseCases/Mhop/Mapping/MhopForumMapper.cs`；输出改用 `ClouderyApi/Models/Mhop/DTOs/MhopForumDtos.cs` 的强类型 DTO（新增 13 个：`BoardOut` / `ForumStatsOut` / `LikeToggleOut` / `LikeIdsOut` / `MineCountOut` / `MineSummaryOut` / `MyPostOut` / `MyPostListOut` / `MyReplyOut` / `MyReplyListOut` / `PostUpdateOut` / `ContentStatusOut` / `OkOut`），属性声明顺序与原匿名对象逐一对齐以保证蛇形 JSON 字段顺序不变；`MhopForumController` 由 703 行降至 133 行（仅 HTTP 绑定 + `MhopOk` / `MhopStatus` 包装，`IsTruthy` 因属 `inc_view` 绑定语义而留在控制器）。机械比对验证：路由/动词 26 项、错误文案（`MhopApiException` / `DomainRuleException`）16 处、`MhopStatus` 2 处、JSON 键名集合全部与重构前一致。
- **M3 里程碑记录（MHOP 应用层，已完成，8 次提交，均仅本地未推送）**：
  - `9c3c6f5` AssessmentAppService；`17b91fa` ForumAppService（MhopForumController 703→133 行，19 个 action 编排 + MhopForumMapper + 13 个 DTO；路由/文案/键名集合机械比对一致）。
  - `0417c74` AdminAppService（MhopAdminController.cs 521→131 行；AdminAppService.cs 419 行、MhopAdminMapper.cs 119 行、12 个输出 DTO；比对：路由 19/19、鉴权特性 18/18、异常文案 26/26、字段顺序全一致；唯一结构差异是 promote_super 由 `Permissions=null` + `[JsonIgnore(WhenWritingNull)]` 省略 permissions，序列化字节等价）。
  - `1474f29` MhopBottleService 归位为 BottleAppService（规范化逐行比对 0 行差异，控制器不动）。
  - `8f7e635` AuthAppService（MhopAuthController 8 个 action + MhopCasdoorController 3 个 action 瘦身；`MhopAuthMapper.ToUserOut(user, maskPhone, exposePermissions)`、`ToTokenOut` 复用；EmailCode 拆 `EmailCodeOut`/`EmailCodeDevOut` 两个 DTO，保证「已投递」分支不多出 `dev_mode:null`）。
  - `05a1140` MhopCasdoorService 归位为 CasdoorAppService（生命周期仍为 AddSingleton）。
  - `e302bd3` 瓶子管理控制器去除 DbContext 直连（统计/用户名查询下沉为 BottleAppService.CountVisibleMessagesAsync / LoadUsernamesAsync）。
  - `0bcbd89` SurvivalCraft 配置键修复（附录 A.1/A.2）：改读 `Env:SCKEY_API_BASE` / `Env:SCKEY_BEARER_TOKEN`（旧键回退）、命名 HttpClient `ServerController.HttpClientName = "SckeyServer"`、令牌非空才发 `Authorization: Bearer`；新增 ServerControllerContractTests 5 例，**行为变化：令牌过去从未发出，现在会发出**。
  - `43a8e2c` Cloudery/Identity 契约基线测试（新增 35 个测试 + `ClouderyApi.Tests/TestData/swagger-routes.snapshot.txt` 路由快照）。
  - 验证口径：全量 **180 passed / 0 failed**；build 0 error，唯一既有警告 `ClouderyApi/Data/ClouderyApiContext.cs(51,22)` CS8603。
  - 注意：本机 MySQL（127.0.0.1:3307）`max_connections=151` 被多个代理共用，**并发跑全量套件会假失败**（`MySqlException : Too many connections`），验证必须单跑。
- **改动清单（Cloudery / Zhuxs）**：
  1. MembersAppService、ExamPaperAppService（含试卷评分领域逻辑）、ExamResultAppService、Zhuxs 各 AppService。
  2. 为 Member / Whitelist / Application / Term / ExamPaper 补输出 DTO，停止返回 EF 实体。
  3. ConfigureApiBehaviorOptions 统一 400 契约，移除死掉的 ModelState 检查。
- **验证**：Stage 0 测试 + 重构前后 swagger.json 路由 / schema 对比（除描述外应无差异）。
- **风险**：最高（改动面最大）。按“一个控制器一个提交”推进。
- **回滚**：逐控制器回退。

### Stage 3 — 按限界上下文重组目录
- **目标**：把类型分层改为模块化单体（第 4.1 节）。
- **改动清单**：建立 Modules/{Mhop,Cloudery,Zhuxs,Identity}/{Domain,Application,Infrastructure,Api} 与 Shared/；移动文件、更新命名空间；同步迁移文件中的 [DbContext] 引用（或让 DbContext 留在 ClouderyApi.Data）。
- **可选**：引入 NetArchTest 架构测试，强制依赖方向与模块隔离。
- **验证**：编译 + 测试 + dotnet ef migrations list 三上下文正常。
- **风险**：机械但触点广；建议放在 Stage 1/2 之后，避免冲突。

### Stage 4 — 领域事件与事务边界
- **目标**：解耦副作用、显式事务。
- **改动清单**：
  1. 聚合内 AddDomainEvent：ContentPublished / ContentRejected / ContentRecalled、BottlePicked / BottleEnded / BottleReported、UserPermissionsChanged。
  2. 在 SaveChangesAsync 覆盖中收集并派发（进程内），或引入 outbox 表（需 EF 迁移）。
  3. 用事件订阅替换控制器的命令式副作用（MhopForumController.cs:206/246/489、MhopBottleAdminController.cs:154/191）。
  4. 多写用例包显式事务，对齐 MhopBottleService.cs:115。
- **风险**：中高；outbox 需要迁移。

### Stage 5 — 横切与工程化（可并行）
- 拆分 ClouderyApiContext → ClouderyContext + ZhuxsContext（需 EF 迁移与历史表处置）；或先做代码级接口隔离。
- Options 模式替换配置直读（AdminOnlyAttribute.cs:26-30、AuthController.cs:34-43、ServerController.cs:13-19、MhopCasdoorService.cs:30-40）。
- 基于 policy 的授权替换 AdminOnlyAttribute 的 service-locator。
- 修复 SurvivalCraft 配置键不匹配（见附录 A）。
- 启动期 Migrate/Seed 移出到部署步骤或受控后台任务（Program.cs:156-183）。
- 每上下文独立 MigrationsHistoryTable；.editorconfig + analyzer；CI 测试门禁；限流分布式化（可选）。
- 清理死代码 / 补 ExamPaper 输入 DTO。

---

## 6. 兼容性红线（必须逐字保留）
- 路由全部不变；CORS 白名单不变（Program.cs:102-123）。
- **鉴权方案真相（Stage 0 运行时实测）**：默认认证方案 = Cookies，但默认 **Challenge 方案 = Bearer**（Program.cs:89-90 的 `.AddCasdoor(...)` 扩展带入 JwtBearerHandler）。所以只挂 `[Authorize]` 的路由未登录时是 **401 + `WWW-Authenticate: Bearer` + 空响应体**，**不是** 302 跳 `/identity/auth/login`（该 action 本就不存在）。只挂 `[AdminOnly]`（无类级 [Authorize]）的路由才由过滤器自己写 `401 {"success":false,"message":"请先登录"}` 或 `403 {"success":false,"message":"无管理员权限，操作被拒绝"}`（AdminOnlyAttribute.cs:19-39）。鉴权改造绝不能引入 302。
- **[AdminOnly] 判定**：读 claim `CasdoorId`，与 `Authorization:Admins`（string[]，OrdinalIgnoreCase）比较。（测试可在启动时注入 `Authorization:Admins:0`。）
- JSON：snake_case + UnsafeRelaxedJsonEscaping（中文不转义）+ UTC 带 Z（MhopJson.cs:14-35）；错误体形状 {detail}。
- **inc_view 必须是字符串**：ASP.NET bool 绑定不接受 "1"，MhopForumController.cs:138 用 string? 并由 IsTruthy 接受 1/true/yes/on（:631）。
- 管理员看真实身份（MhopAdminController.cs:513 RealAuthor 暴露真实用户名 + 手机号），而 mhop/auth/users/{id} 掩码手机号并隐藏权限（MhopAuthController.cs:228）。
- AI 回复生命周期：EnsureForumReplyAsync 只保留最新一条 AI 回复，并在**不发 LLM** 的情况下清理多余回复 / 日志 / 点赞（MhopAiService.cs:301-333）。
- 敏感词命中直接拦截（Status=2 + note 截断 255），并且 QueueReplyReview **仅在 Status==Pending** 时执行（MhopForumController.cs:243-246）。
- 筛查顺序：Violation → Suspect → 联系方式 / URL → crisis 最后（MhopContentPolicy.cs:99-131）；AI 审核对 Crisis 短路为 Safe（MhopAiService.cs:141-144）。
- 捞瓶原子性：原生 ORDER BY RAND() LIMIT 1 + 条件 UPDATE ... WHERE Status=1，3 次重试，SELECT Id AS Value 别名（MhopBottleService.cs:118-140）。
- 管理端 restore 恒传 Drifting，由服务推导真实目标（MhopBottleAdminController.cs:129-141）。
- current user 必须是被追踪实体（MhopCurrentUserAccessor.cs:87-88）。
- 排序保证：论坛回复 IsAi?0:1 再 CreatedAt；漂流瓶管理列表 Pending → ReportedCount → Crisis → AiFlag → CreatedAt；管理消息 hidden → suspect/violation → Id desc；瓶消息按 Id 升序 + Id > afterId。
- mhop/online/heartbeat：trim、≤64、空串转 "anon"。
- [MhopPerm(Bottles)] 与显式 re-check 的行为冗余属防御性设计，不得静默删除。
- 热线文案：12356、010-82951332、110-120 + 配置的 Leke 热线；仅在危机时返回（MhopBottleController.cs:17）。

---

## 7. 风险登记
| 风险 | 影响 | 缓解 |
|---|---|---|
| 无测试导致回归 | 高 | Stage 0 先行；每阶段小步提交 |
| 值对象映射改变列类型 | 中 | 一律 HasConversion 回原列；dotnet ef migrations 应无新增 |
| 移动 DbContext 命名空间破坏迁移 | 中 | 优先保留 Data 命名空间；或全量替换 [DbContext] 引用 |
| 控制器瘦身改变 JSON 字段 | 高 | swagger + 特征测试双重对比 |
| 领域事件引入异步不一致 | 中 | 先进程内同步派发；outbox 单独评估 |
| Stage 2 面太大 | 高 | 按控制器 / 用例拆分提交，逐个验证 |

## 8. 验收标准
- 所有现有路由、请求 / 响应 JSON 与鉴权行为不变（swagger diff + 特征测试）。
- dotnet build 0 error；dotnet test 全绿；三个上下文的 ef migrations list 正常。
- 控制器不再直接依赖 DbContext（除必要的基础设施装配）；领域规则位于 Domain，可在无 HTTP / 无数据库的情况下单测。
- 重复常量、重复状态机、重复校验消除；MHOP 与 Cloudery 的行为红线逐条有测试佐证。

## 9. 提交与里程碑建议
- 每个 Stage 一个（或一组）独立提交，提交信息沿用现有中文约定（refactor(scope): ...）。
- 建议里程碑：M1 = Stage 0；M2 = Stage 1；M3 = Stage 2（MHOP）；M4 = Stage 2（Cloudery/Zhuxs）；M5 = Stage 3；M6 = Stage 4 + Stage 5。
- 每完成一个里程碑提交一次，**不推送**由你决定。

---

## 附录 A：顺带发现的缺陷 / 隐患（非 DDD，但建议一并修）
1. **SurvivalCraft 配置键不匹配（功能性 bug）**：ServerController.cs:18-19 读 SurvivalCraft:SCKEY_API_BASE / SCKEY_BEARER_TOKEN，而 appsettings.json:12-15 定义的是 Env:SCKEY_API_BASE / Env:SCKEY_BEARER_TOKEN → 令牌回退为空，Authorization 头实际未发送。**已修复：0bcbd89**（改读 `Env:SCKEY_*`，命名 HttpClient，令牌非空才加 Bearer）。
2. ServerController.cs:13-22 在类型加载时用 ConfigurationBuilder 直读 appsettings.json 并自建静态 HttpClient，忽略环境变量覆盖与 DI。**已修复：0bcbd89**（注入 `IConfiguration` + `IHttpClientFactory`，测试可用 `ConfigureTestServices` 覆盖主消息处理器）。
3. 死代码：多处 { if (!ModelState.IsValid) ... } 因 [ApiController] 自动校验而不可达（MembersController.cs:39/63、ApplicationsController.cs:39/62、TermsController.cs:39/63）。
4. 死并发处理：多处 catch DbUpdateConcurrencyException，但无 [Timestamp]/IsRowVersion 与并发令牌（MembersController.cs:51-55 等）。
5. ExamPaper 的 POST/PUT 直接绑定实体，存在 over-posting（ExamPapersController.cs:147/162；Id/Sections/UpdatedAt 可被客户端影响）。
6. Whitelist（邀请码）实体直接返回；虽读取受 [AdminOnly] 保护，仍建议输出 DTO。
7. 三个上下文共享 __EFMigrationsHistory，无隔离。
8. appsettings.json 含明文数据库口令 / Casdoor secret / OSS key / SMTP 口令（文件已 gitignore，但属治理风险）。

## 附录 B：证据基线与关键文件行数（本方案撰写时的实测）
- MhopForumController.cs 797 行；MhopAdminController.cs 533 行；MhopBottleService.cs 532 行；MhopContentReviewService.cs 377 行；MhopContentService.cs 116 行。
- ExamPapersController.cs 215 行；ExamResultsController.cs 107 行；AuthController.cs 372 行。
- 其余证据以第 3 节的 file:line 为准。
## 附录 C：M4 契约基线测试暴露的既有缺陷（Stage 2 Cloudery/Zhuxs 实测，均未修）
1. **控制器内 ModelState 检查是死代码**：`[ApiController]` 自动校验先行，`MembersController.cs:39/63`、`ExamPapersController` 的 POST/PUT、`Terms/Applications/Whitelists` 的 PUT/POST 中 `BadRequest(new {success=false,message="参数校验失败"})` 永不执行。实测管理员 `POST /cloudery/members {position:…}` → 400 `application/problem+json`（`errors.Name=["The Name field is required."]`），**没有** success/message 字段。全仓库无 `ConfigureApiBehaviorOptions`，400 契约在「控制器文案」与「框架 ProblemDetails」之间分裂；统一必然波及已冻结的 MHOP，需按 Stage 2 第 11 项单独立项。
2. **`POST /exam/results` 的 body==null 分支不可达**：无 Content-Type → **415**；`application/json` + 字面 `null` 或空白体 → 400 ValidationProblemDetails。`Rejected("缺少请求体")` 永不执行（`ResultAnalysisController` 的 body-null 同理，但其 testId 空白分支可达：`{}` → 400「缺少量表标识（testId）」）。
3. **同一 API 内 404 有两种形状**：`GET/PUT/DELETE /cloudery/members/{unknown}` 的空参 `NotFound()` 被 ClientErrorResultFilter 转为 404 + `application/problem+json`（`type` 指向 rfc9110#section-15.5.5，**非空体**）；而 `/exam/ExamPapers/{unknown}`、`/exam/results/{unknown}` → 404 + `{success:false,message}`。
4. **同一资源时间格式跨端点不一致**：`POST /exam/results`(`/sync`) 的 `savedAt` 形如 `2026-01-02T03:04:05Z`、`updatedAt` 带 `Z` + 7 位小数；`GET /exam/results` 经 MySQL `datetime(6)` 往返后同字段**无 `Z`、6 位小数**（Kind/精度丢失），客户端按 ISO-8601 带时区解析会得到错误时刻。基线刻意只断言字段顺序与业务字段，未断言时间字面量以免固化缺陷。
5. **`Location` 使用声明大小写**：`CreatedAtAction` 生成 `/cloudery/Members/{id}`、`/exam/ExamPapers/{id}`（与请求的全小写路径不同，路由匹配不区分大小写）；Stage 2 若改路由必须同步。
6. 未覆盖分支（无缺陷，仅测试缺口）：`MembersController` PUT 的并发 rethrow、Zhuxs 三控制器写成功路径与 `DbUpdateException`/`DbUpdateConcurrencyException`、Auth 的真实 Casdoor callback 成功路径、`ResultAnalysisService` 的 LLM 成功路径（测试把 `Llm__BaseUrl` 指向 `http://127.0.0.1:1` 强制 `engine="local"`）、`IpRateLimitAttribute` 的 429（静态字典 key=`ip|path`，会污染同路径测试）、`ExamResultService` 的 200 条/256KB 上限、`ExamPapers` 的 PUT/DELETE 成功（204）。
