# ClouderyApi DDD 改造方案（评估 + 分阶段实施计划）

> 状态：待评审。本文件仅为方案，尚未改动任何业务代码。
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
- 115 个 .cs 文件，约 12,200 行；单项目（ClouderyApi.sln:6），无测试项目，无 analyzer / .editorconfig，Nullable 已开启。
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
- 无测试；CI 的 test 步骤是空转（.github/workflows/dotnet.yml:24-25）。
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
| Stage 1 | 值对象 + 充血模型 | 规则收口、消除重复常量 | 中 | 无 |
| Stage 2 | 应用层抽取、控制器瘦身 | 消除 118 处 _db.、去重 | 高 | 无 |
| Stage 3 | 目录按限界上下文重组 | 模块边界清晰 | 中 | 无（除非移动 DbContext 命名空间，也不需要） |
| Stage 4 | 领域事件 + 事务边界 | 解耦副作用、保证一致性 | 中高 | 可选 outbox |
| Stage 5 | 横切 / 工程化 | 可观测、可测试、修缺陷 | 低中 | 拆上下文时才需要 |

### Stage 0 — 建立安全网（必须先做）
- **目标**：让后续每一步重构都有回归保护。
- **改动清单**：
  1. 新增测试项目 ClouderyApi.Tests（xUnit + Microsoft.AspNetCore.Mvc.Testing；DB 用 Testcontainers.MySql 起真实 MySQL，因为漂流瓶捞取依赖 ORDER BY RAND() 与条件 UPDATE 等 MySQL 方言，InMemory/SQLite 不可用）。
  2. 特征测试覆盖关键契约：identity/auth 登录与 /me；mhop/forum 帖子创建→审核→公开 与 withdraw/submit 状态机；mhop/bottles throw→pick→messages→end/report；mhop/auth 注册/登录与权限；exam/results sync 幂等（ClientKey）；序列化契约（snake_case、UnsafeRelaxedJsonEscaping、错误体 {detail}）。
  3. 修复 CI：让 .github/workflows/dotnet.yml:24-25 的 dotnet test 真正运行测试项目。
- **验证**：dotnet test 全绿。
- **风险 / 降级**：CI 需 Docker；若无，退化为对领域 / 应用服务的单元测试 + 人工冒烟清单。
- **回滚**：独立提交，删除测试项目即可。

### Stage 1 — 值对象与充血模型（MHOP 优先）
- **目标**：把规则收回模型，消除原始类型与重复常量。
- **改动清单**：
  1. 统一 ContentStatus 单一来源，删除 MhopForumController.cs:312-315 的重复常量。
  2. 新增值对象：ContentText（trim、非空、1–2000 / 1–1000）、BoardSlug（校验 MhopBoards.Slugs）、ImageRefs（≤9、JSON 封装）、LikeTargetType、AiFlag、MhopRole、PermissionSet、BottleStatus、BottleEndReason。
  3. 为 MhopPost / MhopReply / MhopUser / MhopBottle 增加第 4.3 节的方法，非法转换抛领域异常。
  4. 抽取 AssessmentScoring 领域服务（自 MhopAssessmentController.cs:39-115）；把 MhopForumController.cs:556-565 的内联敏感词筛查并入 MhopContentPolicy。
  5. EF 配置用 HasConversion 保持列不变。
- **验证**：dotnet build + Stage 0 测试。
- **风险**：值对象相等性与 EF 追踪要注意；务必保持 MhopCurrentUserAccessor 返回**被追踪**实体（MhopCurrentUserAccessor.cs:87-88 注释）以满足“改后 SaveChanges”。
- **回滚**：按聚合拆分提交，可单独回退。

### Stage 2 — 应用层抽取、控制器瘦身
- **目标**：控制器只做 HTTP；用例编排进 Application 层。
- **改动清单（MHOP）**：
  1. ForumAppService 承接 MhopForumController 的读写 / 状态流转 / 点赞 / 我的列表，映射移入 Application/Mhop/Mapping。
  2. AdminAppService 承接 MhopAdminController.cs:42-533 的审核、用户与权限、统计、AI 日志。
  3. MhopBottleService 归位为 BottleAppService（行为逐字保留，控制器不动）。
  4. AuthAppService / AssessmentAppService / CasdoorAppService。
  5. 控制器改为薄适配器（参照 MhopBottleController.cs:20-110）。
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
1. **SurvivalCraft 配置键不匹配（功能性 bug）**：ServerController.cs:18-19 读 SurvivalCraft:SCKEY_API_BASE / SCKEY_BEARER_TOKEN，而 appsettings.json:12-15 定义的是 Env:SCKEY_API_BASE / Env:SCKEY_BEARER_TOKEN → 令牌回退为空，Authorization 头实际未发送。
2. ServerController.cs:13-22 在类型加载时用 ConfigurationBuilder 直读 appsettings.json 并自建静态 HttpClient，忽略环境变量覆盖与 DI。
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
