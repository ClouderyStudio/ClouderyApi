# Stage 4 领域事件与事务边界：现状证据与执行要点

本文是 [docs/DDD-REFACTOR-PLAN.md](DDD-REFACTOR-PLAN.md) Stage 4「领域事件与事务边界」的施工图。目标：**解耦副作用、显式事务**——把散落在用例里的命令式副作用收敛为领域事件，并给真正多写的路径加显式事务。

与 Stage 3 的「零行为变更」定位不同，本阶段触及运行时语义，因此硬约束是：**HTTP 路由、JSON 契约、鉴权语义、状态码/文案、副作用时序一律不变**；每个子步骤独立提交、可编译、全量测试通过。所有行号以当前 master（Stage 2 之后）为准；方案文档 Stage 4 引用的 `MhopForumController.cs:206/246/489` 已因 Stage 2 失效（见 §一 末）。

## 一、现状证据表：散落在用例里的命令式副作用

统一约定：`Save` = `SaveChangesAsync`；`Task.Run` 一律指 `_ = Task.Run(...)` 火后不理。

| 场景 | 代码位置 | 副作用 | 相对 Save 的顺序 | 同步/火后不理 |
|---|---|---|---|---|
| 发帖 → AI 审核触发 | `ClouderyApi/UseCases/Mhop/ForumAppService.cs:174-178` | `_review.QueuePostReview(post.Id)` | Save(:175) 之后 | 火后不理（`MhopContentReviewService.cs:42`） |
| 回复 → AI 审核触发 | `ClouderyApi/UseCases/Mhop/ForumAppService.cs:199-203` | `QueueReplyReview`，仅 `Status==Pending` | Save(:200) 之后 | 火后不理（`MhopContentReviewService.cs:44`） |
| 编辑帖子 → 重审 + 清旧图 | `ClouderyApi/UseCases/Mhop/ForumAppService.cs:411-416` | Pending 时 `QueuePostReview`(:415)；`_uploads.DeleteAsync(removedImages)`(:416) | 均在 Save(:413) 之后 | 排队火后不理 + 同步删对象存储 |
| 提审帖子 / 回复 | `ClouderyApi/UseCases/Mhop/ForumAppService.cs:438-440`、`:489-491` | `QueuePostReview` / `QueueReplyReview` | Save 之后 | 火后不理 |
| 编辑回复 | `ClouderyApi/UseCases/Mhop/ForumAppService.cs:464-468` | Pending 时 `QueueReplyReview` + 删旧图 | Save(:466) 之后 | 同上 |
| 撤回 / 重提（帖子、回复） | `ClouderyApi/UseCases/Mhop/ForumAppService.cs:427-428`、`:478-479` | 撤回本身无副作用 | Save 之后无操作 | 同步 |
| 删除帖子 / 回复 / 用户 | `ClouderyApi/Services/Mhop/MhopContentService.cs:47-53`、`:64-71`、`:103-108` | 级联删除回复/点赞/AI 日志/图片 + 删对象存储 | RemoveRange 后一次 Save，再删对象存储 | 同步（对象存储失败仅记日志） |
| 点赞 | `ClouderyApi/UseCases/Mhop/ForumAppService.cs:233-244` | 无存储计数；count 由 `LikeCountMapAsync`(:524-533) 查询得出 | Save(:241) 之后查询 | 同步 |
| 审核通过帖子 | `ClouderyApi/UseCases/Mhop/AdminAppService.cs:106-112` | `post.Publish(note)` 后 `if(approved) await _ai.EnsureForumReplyAsync(...)`(:112) | Save(:108) 之后 | **同步 await**（已存在 AI 回复时清理写库，无则排队） |
| 审核拒绝帖子 | `ClouderyApi/UseCases/Mhop/AdminAppService.cs:106-107` | `post.Reject(note)` | Save(:108) | 同步，无连带 |
| 重生成 AI 回复 | `ClouderyApi/UseCases/Mhop/AdminAppService.cs:125` | `RegenerateForumReplyAsync` | 内部先清理后排队 | 内部含同步写库 |
| 审核 / 撤回 / 恢复回复 | `ClouderyApi/UseCases/Mhop/AdminAppService.cs:175-177`、`:191-192`、`:203-204` | Publish/Reject/Recall/Restore | Save 之后无连带 | 同步 |
| 管理员改权限 / 状态 / 角色 / 徽章 / 密码 | `ClouderyApi/UseCases/Mhop/AdminAppService.cs:247-252`、`:264-265`、`:283-308`、`:331-332`、`:372-373` | 仅更新用户自身 | 各自 Save | 同步，**无任何连带清理** |
| 管理员删用户 | `ClouderyApi/UseCases/Mhop/AdminAppService.cs:347` | 委托 `MhopContentService.DeleteUserAsync` | 同上 | 同步 |
| 瓶子投掷 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:75-80` | `QueueBottleReview(bottle.Id)` | Save(:77) 之后 | 火后不理（`MhopContentReviewService.cs:48`） |
| 瓶子捞取 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:105-136` | 唯一显式事务（Begin :105 / Commit :135）+ 条件 UPDATE(:125-129) + Pick(:133) | 事务内 Save(:134) | 同步 |
| 瓶子结束 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:268-270` | `End(userId)`，无事件、无通知 | Save 之后仅日志 | 同步 |
| 瓶子举报 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:288-291` | `TryReport`，仅新举报才 Save | Save | 同步 |
| 发消息 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:243-248` | `QueueMessageReview(message.Id)` | Save(:246) 之后 | 火后不理（`MhopContentReviewService.cs:52`） |
| 会话超时结束 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:173-181` | `ExecuteUpdateAsync` 批量置 Ended/Timeout | 单次写 | 同步（由 `MhopBottleTimeoutService.cs:29` 每小时驱动） |
| 已读标记 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:207-209` | 更新两侧 LastReadAt | Save | 同步 |
| 管理员下架/恢复瓶子、显隐消息、批准 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:420-421`、`:441`、`:454-455` | 状态写入 | Save 之后仅日志 | 同步 |
| 管理员重审瓶子 / 消息 | `ClouderyApi/Controllers/Mhop/MhopBottleAdminController.cs:152`、`:189` | `QueueBottleReview(id, rescreen:true)` / `QueueMessageReview(..., rescreen:true)` | 控制器内联，且控制器不开事务 | 火后不理 |
| AI 审核服务内部写 | `ClouderyApi/Services/Mhop/MhopContentReviewService.cs:74-81`、`:90-95`、`:122-128`、`:176-182`、`:188-193`、`:200-205`、`:260` | ExecuteUpdate 改 Status/AiFlag；安全内容再 `EnsureForumReplyAsync`(:84)；异常走 `MarkBottleUnavailableAsync`(:320)/`MarkMessageUnavailableAsync`(:339) | 独立 DI scope，与业务 Save 无事务关系 | 后台 |
| AI 回复生成 | `ClouderyApi/Services/Mhop/MhopAiService.cs:367-420` | `Task.Run`(:369)：回复 Save(:402) → 写 `mhop_ai_logs`(:403) → Save(:413) | **两次独立 Save** | 火后不理 |
| 日志 / 审计 | `ClouderyApi/UseCases/Mhop/BottleAppService.cs:78/136/270/291/422/442/456`、`ClouderyApi/Services/Mhop/MhopContentService.cs:47-53` | `ILogger` 记录；`mhop_ai_logs` 全仓仅 `MhopAiService.cs:403` 写入 | 在 Save 之后 | 同步 |
| 轮询兜底 | `ClouderyApi/Services/Mhop/MhopBottleTimeoutService.cs:22-43` | 每小时 `ApplyTimeoutAsync`(:29) + `RequeueStaleBottleReviewsAsync(10min)`(:34-35) | 独立 scope | 后台 |

- 全仓 `Task.Run` 只有 5 处：`MhopAiService.cs:369`、`MhopContentReviewService.cs:42/44/48/52`（`Services/Mhop/MhopObjectStorage.cs` 里的 Task.Run 是 SDK 同步卸载，不是业务副作用）。
- 所有 `Queue*Review` 都在对应 `SaveChangesAsync` **之后**调用；`AdminAppService.cs:112` 是唯一在请求线程里同步 await 的 AI 连带。
- 未发现独立的站内通知表/字段（**待实测**：若存在请补）；AI 审计日志仅 `MhopAiService.cs:403` 一个写入点。
- 控制器层几乎已无副作用：`MhopForumController.cs`(132 行) 与 `MhopAdminController.cs`(130 行) 已是薄适配器，只注入 `_forum`/`_admin`。方案 Stage 4 的 `MhopForumController.cs:206/246/489` 行号已失效，现存控制器级副作用仅 `MhopBottleAdminController.cs:152/:189`。

## 二、事件目录

| 事件 | 触发聚合与方法 | 载荷字段 | 幂等要求 | 现状对应 |
|---|---|---|---|---|
| ContentPublished | `MhopPost.Publish`(:130-134)（经审核发布）、`MhopReply.Publish` | PostId/ReplyId、Note、PublishedAt | 键=(内容Id, 已发布)；重复派发不得生成第二份 AI 回复 | `AdminAppService.cs:112`、`MhopContentReviewService.cs:84` |
| ContentRejected | `MhopPost.Reject`(:137-141)/`RejectBySensitiveWords`(:144-145)、`MhopReply.Reject` | 内容Id、Note(≤255)、AiFlag | 同内容键幂等 | `AdminAppService.cs:106-107`、`MhopContentReviewService.cs:90-95` |
| ContentRecalled | `MhopPost.Withdraw`(:114-118)、`MhopReply.Recall/Withdraw` | 内容Id、Reason、At | 已撤回再撤不报错 | `AdminAppService.cs:191-192` |
| ContentSubmittedForReview | `MhopPost.SubmitForReview`(:121-127)、`ApplyAuthorEdit`(:91-111)、新建回复 | 内容Id、内容类型 | 只有 Pending 才排队；`Status==Pending` 守卫兜住重复 | `ForumAppService.cs:178/203/415/440/467/491` |
| BottlePicked | `MhopBottle.Pick`(:145-151) | BottleId、PickerId、At | 条件 UPDATE 保证不可重复捞取 | `BottleAppService.cs:105-136` |
| BottleEnded | `MhopBottle.End`(:154-159)；超时路径由批量 UPDATE 触发 | BottleId、EndReason(Manual=0/Timeout=1)、EnderId | 已结束再结束返回 false | `BottleAppService.cs:268-270`、`:173-181` |
| BottleReported | `MhopBottle.TryReport`(:165-175) | BottleId、ReporterId、Reason、去重后举报数 | 同一举报人重复举报不新增 | `BottleAppService.cs:288-291` |
| BottleMessageSent | 新增消息 + `MhopBottle.TouchLastMessage`(:162) | BottleId、MessageId、SenderId、At | 键=MessageId；重发只应重审一次 | `BottleAppService.cs:243-248` |
| UserPermissionsChanged | `MhopUser.SetPermissions`/`SetRole`/`Disable`/`Enable`/`SetBadge` | UserId、Permissions、Role、Status、OperatorId | 键=(UserId, 权限集)；当前无订阅者 | `AdminAppService.cs:247-252/264-265/283-308/372-373` |
| ContentDeleted | `MhopContentService.DeletePostAsync/DeleteReplyAsync/DeleteUserAsync` | 内容Id、图片 Key 列表、受影响回复数 | 对象存储清理必须容忍重复/对象不存在 | `MhopContentService.cs:47-53/64-71/103-108` |
| AiReplyGenerated | `MhopAiService.QueueForumReply` | PostId、ReplyId、Engine、Prompt/Response | 键=PostId；当前无订阅者（占位） | `MhopAiService.cs:402/413` |
| ReviewRescreenRequested | 管理员重审 | 目标类型、Id、At | 幂等 | `MhopBottleAdminController.cs:152/:189` |

方案列出的 8 个事件（ContentPublished/ContentRejected/ContentRecalled、BottlePicked/BottleEnded/BottleReported、UserPermissionsChanged）均能在现有代码定位到触发点；上表后 5 个是代码中确实存在、方案未列出的副作用点（ContentSubmittedForReview、BottleMessageSent、ContentDeleted、AiReplyGenerated、ReviewRescreenRequested）。

## 三、派发机制选型：进程内派发 vs outbox 表

| 维度 | A：`SaveChangesAsync` 收集 + 提交后进程内派发 | B：outbox 表 + EF 迁移 |
|---|---|---|
| 现状基础 | 三个 DbContext **均未覆写** `SaveChanges`/`SaveChangesAsync`（`ClouderyApi/Data/MhopDbContext.cs` 仅 DbSets + OnModelCreating），新增覆写即可 | 需在 `ClouderyApi/Migrations/Mhop/` 新增迁移；三上下文共享 `__EFMigrationsHistory`（`Program.cs:27-30/32-34/58-59` 的 `UseMySQL` 只传连接串，未配 MigrationsAssembly/HistoryTable） |
| 依赖 | 零新依赖，符合方案 :31「默认轻量进程内派发」 | 新表 + 后台派发器 + 迁移 |
| 测试影响 | 无需改测试；`ClouderyApiFactory` 仍只迁 `MhopDbContext` | 集成测试启动即 `Database.Migrate()`（`Program.cs:162-173`），会真跑新迁移，增加 MySQL 依赖与耗时 |
| 失败 / 重试 | 与现状一致：`Task.Run` 本就即发即忘，进程崩溃即丢；瓶子有轮询兜底 | 可持久化重试，跨进程可靠 |
| 回滚 | 单提交 `git revert` | 还需 `dotnet ef database update` 回退迁移 |

推荐 **A**。理由：(1) 现状 AI 审核/复核本身就是 `Task.Run` 触发，A 的投递语义不比现状差，不引入新的可见行为；(2) 避免在「解耦副作用」目标下叠加迁移风险（方案给 Stage 4 中高风险，主因就是 outbox 需要迁移）；(3) 可逐事件小步提交、逐步回退。outbox 留待真正需要跨进程可靠投递时（Stage 5 或出现外部消费者）再补。

## 四、事务边界

事实基线：EF Core 的 `SaveChangesAsync` 已把**单次**调用的全部变更包进一个事务，因此 `MhopContentService.cs:47-53/64-71/103-108` 的多次 `RemoveRange` + 一次 Save 已经原子；当前唯一显式事务是 `BottleAppService.cs:105-136`。

真正「多次 DB 往返、当前无显式事务」的点：

| 路径 | 证据 | 两次写 | 建议 |
|---|---|---|---|
| 审核通过 + AI 连带 | `AdminAppService.cs:108` + `:112` | 审核写入；`EnsureForumReplyAsync` 在已有 AI 回复时会再写库（`MhopAiService.cs:318-323`） | 连带移到提交后订阅者，但**保留同步 await 语义**（见 §六） |
| AI 回复 + 审计日志 | `MhopAiService.cs:402` + `:413` | 回复落库；随后 AI 日志落库 | 用 `BeginTransactionAsync` 包住两处 Save，不得改变失败时是否留半条回复 |
| AI 审核发布 + AI 回复 | `MhopContentReviewService.cs:74-81` + `:84` | ExecuteUpdate 发布；`EnsureForumReplyAsync` 可能再写 | 同上 |
| 对象存储清理 | `MhopContentService.cs:53/71/110`、`AuthAppService.cs:210`、`ForumAppService.cs:416/468` | 不参与事务，属「先落库再尽力清理」 | **保持现状**（`MhopContentService.cs:7-11` 注释明确），不得借加事务把它们挪进事务 |
| 瓶子捞取 | `BottleAppService.cs:105-136` | 已是显式事务 | 保持，作为写法对齐样板 |

写法：`await using var tx = await _db.Database.BeginTransactionAsync();` … `await tx.CommitAsync();`，失败路径沿用现有 try/catch + rollback（参考 `BottleAppService.cs:140/145`）。**不得**改变任何对外状态码/文案（例：`BottleAppService.cs:450` 的 409「该瓶子不在待审核状态」、`MhopBottleController.cs:37-48` 的 409 `{detail}`）。

## 五、执行顺序（每步独立提交、可编译、可回滚）

1. 事件基座：加领域事件列表（基类/接口）+ `IDomainEventHandler<T>` + `MhopDbContext.SaveChangesAsync` 覆写（收集 → Save → 派发 → 清空），暂无订阅者。提交：`refactor(mhop): 引入领域事件基座（无订阅者，行为不变）`。验收：build 0 error；全量测试全绿（**当前测试数待实测**：方案 M3 记 180，Stage 3 文档记 145）；`SwaggerRouteSnapshotTests.cs:98` 快照不变。
2. 内容事件：在 `MhopPost`/`MhopReply` 的 Publish/Reject/Withdraw/SubmitForReview/ApplyAuthorEdit 登记事件，订阅者接管 `ForumAppService.cs:178/203/415/440/467/491` 与 `AdminAppService.cs:112`。提交：`refactor(mhop): 内容副作用改为领域事件`。验收：`MhopForumWriteContractTests.cs:67`、`DomainContentTests` 全绿；确认发帖响应不等待 AI 审核；敏感词命中仍不排队（`ForumAppService.cs:198-203`）。
3. 瓶子事件：`BottleAppService.cs:80/248` 与 `MhopBottleAdminController.cs:152/189` 改事件。提交：`refactor(mhop): 瓶子副作用改为领域事件`。验收：`DomainBottleTests` 16 条全绿；瓶子路由快照不变；**瓶子 HTTP 路径无契约测试（待实测/建议补）**，需人工对比 JSON/文案。
4. 用户事件：登记 `UserPermissionsChanged` 等，订阅者暂空。提交：`refactor(mhop): 用户权限变更登记领域事件`。验收：`MhopAdminAuthorizationTests` 5 条全绿；行为零变更。
5. 事务收口：`MhopAiService.cs:402/413`、`MhopContentReviewService.cs:74-84` 包显式事务。提交：`refactor(mhop): AI 回复与审核写入收口事务`。验收：`SmokeTests` 与写路径契约测试全绿；并发捞取/审核不回归。

统一验收：`dotnet build ClouderyApi.sln --nologo -v q` 0 error；`dotnet test ClouderyApi.Tests` 全绿（**本机 MySQL `max_connections` 有限，串行跑，勿并发多实例**）；`dotnet ef migrations list --context MhopDbContext` 无新增（走 A 路线时每步都应无新增）。

## 六、风险与禁止行为

- **时序必须逐字保留**：AI 审核是「SaveChanges 成功后才 `Task.Run` 排队、不阻塞响应」（`ForumAppService.cs:175→178`、`MhopContentReviewService.cs:42/44`）。事件订阅者必须在提交后触发，且触发本身不得在请求线程做 IO；漏发会让内容永久 Pending。
- `AdminAppService.cs:112` 的 `await _ai.EnsureForumReplyAsync(...)` 是**同步**调用：已有 AI 回复时它同步写库（`MhopAiService.cs:318-323`），没有时只排队（`:332`）。事件化后必须保留该分叉，不要统一改异步，否则管理员审核接口的 DB 写入时序会变。
- 幂等：重复审核当前靠 `ExecuteUpdateAsync` 的「Id + 内容未变 + `Status==Pending`」守卫兜住（`MhopContentReviewService.cs:74-81/122-128`）；事件重发不得绕过该守卫生成第二份 AI 回复。
- 兜底不对称：`MhopBottleTimeoutService.cs:34-35` 只复排队 `AiReviewedAt == null` 且超期的 Pending 瓶子；内容侧无同类兜底，内容事件丢失是不可逆的。
- 禁止：改路由/CORS/JSON snake_case/UTC `Z`/错误体 `{detail}`；把 `inc_view` 改成数字；移除 `[MhopPerm(Bottles)]` 冗余校验；移除对象存储「先落库再清理」的 try/catch；在事件订阅里引入新的对外可观测行为。
- 回滚：每步独立提交，`git revert <sha>`；若要整体放弃基座，先 revert 订阅步骤再 revert 基座步骤，保证仍可编译。
