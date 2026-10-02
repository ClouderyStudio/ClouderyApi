# Stage 3 模块化单体：目录映射与执行要点

本文是 [docs/DDD-REFACTOR-PLAN.md](docs/DDD-REFACTOR-PLAN.md) Stage 3「按限界上下文重组目录」的施工图。**只做文件移动与命名空间调整，零行为变更**：HTTP 路由、JSON 契约、鉴权语义一律不变，每个子步骤都要独立构建 + 全量测试通过。

> **状态：✅ 已完成**（7 次提交 `4d2ebd4` → `f1bdfd3` → `e76e220` → `2c533a3` → `739fd23` → `786b7e6` → `8aec30c`，均仅本地未推送；每步 build 0 error + 单跑 185 passed / 0 failed，三上下文 EF 无 pending）。下文的「现状普查 / 映射表 / 执行顺序」保留为施工时的历史视图，**收尾后的真实布局见「## 重组后实测」**。

## 目标布局

```
ClouderyApi/
  Modules/
    Mhop/{Domain,Application,Application/Mapping,Api,Api/Contracts,Infrastructure,Infrastructure/Persistence}
    Cloudery/{Domain,Application,Application/Mapping,Api,Api/Contracts}
    Zhuxs/{Domain,Application,Application/Mapping,Api,Api/Contracts}
    Identity/{Domain,Application,Api,Infrastructure/Persistence}
    Link/Api                 # LongLinkController（独立小模块）
    SurvivalCraft/Api        # ServerController（独立小模块）
  Shared/{Exceptions,Json,Filters,Ai}
  Data/ClouderyApiContext.cs # 唯一残留：Cloudery 与 Zhuxs 共用（Stage 5 拆分）
  Migrations/{ClouderyApi,Identity,Mhop}/  # 保留原位，[DbContext] 与 using 已改用新命名空间
  Program.cs
```

## 命名空间规则（关键）

- 模块根命名空间统一用 `ClouderyApi.Modules.<Context>.<Layer>`。
- **绝不能**使用 `ClouderyApi.Application`：Zhuxs 实体类型的短名就是 `Application`（`ClouderyApi.Models.Zhuxs.Application`），C# 向上查找未限定名时会在 `ClouderyApi` 层先命中命名空间 `ClouderyApi.Application`，从而报 CS0118（历史实测命中 ClouderyApi.Data/ClouderyApiContext.cs 与 ClouderyApi/Controllers/Zhuxs/ApplicationsController.cs）。`ClouderyApi.Modules.Zhuxs.Application` 不会触发该冲突，因为 `ClouderyApi` 层只有 `Modules`。
- 层目录名仍可用 `Application`（目录名不参与命名解析），但命名空间末端是 `...Zhuxs.Application`，位于 `Modules` 之下，安全。

## 现状普查（重组前；ClouderyApi/ 下 150 个 .cs、30 个命名空间；实测于 M4 完成后的 master）

> 本节是施工前的历史快照，下列命名空间**均已不存在**；收尾后的真实布局见下一节「重组后实测」。

| 文件数 | 命名空间 |
|---|---|
| 21 | ClouderyApi.Services.Mhop（应用服务已迁往 UseCases.Mhop） |
| 18 | ClouderyApi.Models.Mhop |
| 13 | ClouderyApi.Migrations.Mhop |
| 11 | ClouderyApi.Controllers.Mhop |
| 7 | ClouderyApi.Migrations.ClouderyApi |
| 7 | ClouderyApi.Models.Mhop.DTOs |
| 6 | ClouderyApi.Models.Cloudery.DTOs |
| 6 | ClouderyApi.Models.Zhuxs.DTOs |
| 6 | ClouderyApi.UseCases.Mhop |
| 5 | ClouderyApi.Data |
| 5 | ClouderyApi.UseCases.Cloudery |
| 4 | ClouderyApi.Controllers.Cloudery |
| 4 | ClouderyApi.Services.Ai |
| 4 | ClouderyApi.UseCases.Mhop.Mapping |
| 4 | ClouderyApi.UseCases.Zhuxs |
| 3 | ClouderyApi.Controllers.Zhuxs / Migrations.Identity / Models.Cloudery / Models.Zhuxs / UseCases.Zhuxs.Mapping |
| 2 | ClouderyApi.Controllers.Filters / Services.Cloudery / UseCases.Cloudery.Mapping |
| 1 | ClouderyApi.Controllers.{Auth,Misc,SurvivalCraft} / Models / Models.Identity / UseCases.Cloudery.Domain / UseCases.Identity /（1 个文件无 namespace 声明） |

## 重组后实测（Stage 3 收尾，commit `8aec30c`）

ClouderyApi/ 下 **150 个 .cs / 31 个命名空间**（+ `Program.cs` 无 namespace 声明），实测：

| 文件数 | 命名空间 |
|---|---|
| 20 | ClouderyApi.Modules.Mhop.Domain |
| 13 | ClouderyApi.Migrations.Mhop |
| 12 | ClouderyApi.Modules.Mhop.Infrastructure（另有 Infrastructure/Persistence 2） |
| 11 | ClouderyApi.Modules.Mhop.Api |
| 10 | ClouderyApi.Modules.Mhop.Application（另有 Application/Mapping 4） |
| 7 | ClouderyApi.Migrations.ClouderyApi |
| 7 | ClouderyApi.Modules.Cloudery.Application（另有 Application/Mapping 2） |
| 7 | ClouderyApi.Modules.Mhop.Api.Contracts |
| 6 | ClouderyApi.Modules.Cloudery.Api.Contracts |
| 6 | ClouderyApi.Modules.Zhuxs.Api.Contracts |
| 4 | ClouderyApi.Modules.Cloudery.Api / .Cloudery.Domain / .Zhuxs.Application / .Shared.Ai |
| 3 | ClouderyApi.Migrations.Identity / .Modules.Zhuxs.Api / .Zhuxs.Application.Mapping / .Zhuxs.Domain / .Shared.Exceptions |
| 2 | ClouderyApi.Modules.Identity.Infrastructure.Persistence / .Mhop.Infrastructure.Persistence / .Shared.Filters |
| 1 | ClouderyApi.Data（仅 ClouderyApiContext.cs）/ .Modules.Identity.{Api,Application,Domain} / .Modules.Link.Api / .Modules.SurvivalCraft.Api / .Shared.Json |

`ClouderyApi/Controllers/`、`Models/`、`Services/`、`UseCases/` 已空（目录不再存在）；`ClouderyApi/Data/` 只剩 `ClouderyApi/Data/ClouderyApiContext.cs`（命名空间仍 `ClouderyApi.Data`）。**`ClouderyApi.Application` 从未出现**（规避 CS0118）。

## 映射表

> 下表是施工时的映射计划；**全部已落地**（含 `MhopDbContext`/`MhopDbContextFactory` 与 `IdentityDbContext`/`IdentityDbContextFactory`），旧路径均已不存在。

### Mhop

| 现状 | 目标 | 依据 |
|---|---|---|
| `Models/Mhop/*.cs`（18，不含 DTOs） | `Modules/Mhop/Domain/` | 聚合、值对象、纯规则 |
| `Services/Mhop/MhopContentPolicy.cs`、`MhopModeration.cs` | `Modules/Mhop/Domain/` | 纯策略/危机识别，无 IO |
| `UseCases/Mhop/{Assessment,Forum,Admin,Auth,Bottle,Casdoor}AppService.cs`、`UseCases/Mhop/Mapping/*`、`Services/Mhop/MhopAdminPermissions.cs`、`MhopContentService.cs`、`MhopContentReviewService.cs`、`MhopUploadService.cs` | `Modules/Mhop/Application/` | 用例编排（Stage 2 已完成） |
| `Controllers/Mhop/*`（11）、`Models/Mhop/DTOs/*`（7）、`Services/Mhop/{MhopJson,MhopApiException,MhopApiExceptionFilter}.cs`、`Controllers/Mhop/{MhopControllerBase,MhopAdminAttribute}.cs` | `Modules/Mhop/Api/`（DTO 可放 `Api/Contracts/`） | HTTP 契约与序列化 |
| `Services/Mhop/{MhopAiService,MhopBottleTimeoutService,MhopCasdoorService,MhopCurrentUserAccessor,MhopEmailCodeService,MhopJwtService,MhopObjectStorage,MhopOnlineTracker,MhopOptions,MhopOrphanSweeper,MhopPasswordHasher,MhopSeeder,MhopSmtpClient}.cs` | `Modules/Mhop/Infrastructure/` | 出站依赖与持久化辅助 |
| `Data/MhopDbContext.cs`、`MhopDbContextFactory.cs` | ✅ 已迁 `Modules/Mhop/Infrastructure/Persistence/`（第 5 步） | 见下节取舍 |

### Cloudery / Zhuxs / Identity / Shared

| 现状 | 目标 |
|---|---|
| `Models/Cloudery/{ExamPaper,ExamResult,Member}.cs` + `DTOs/*`（3） | `Modules/Cloudery/Domain/` + `Api/Contracts/` |
| `Services/Cloudery/{ExamResultService,ResultAnalysisService}.cs`、`UseCases/Cloudery/{Members,ExamPaper,ExamResult,ResultAnalysis}AppService.cs`、`UseCases/Cloudery/Mapping/*`、`UseCases/Cloudery/Domain/ExamPaperGrader.cs`、`UseCases/Cloudery/ClouderyWriteConflictException.cs` | `Modules/Cloudery/Application/`（`ExamPaperGrader` 属 Domain） |
| `Controllers/Cloudery/*`（4） | `Modules/Cloudery/Api/` |
| `Models/Zhuxs/{Application,Term,Whitelist}.cs` + `DTOs/*`（3） | `Modules/Zhuxs/Domain/` + `Api/Contracts/` |
| `Controllers/Zhuxs/*`（3）、`UseCases/Zhuxs/{Whitelists,Terms,Applications}AppService.cs`、`UseCases/Zhuxs/Mapping/*`、`UseCases/Zhuxs/ZhuxsWriteConflictException.cs` | `Modules/Zhuxs/Api/` + `Modules/Zhuxs/Application/` |
| `Models/Identity/User.cs` | `Modules/Identity/Domain/` |
| `Data/IdentityDbContext.cs` + `IdentityDbContextFactory.cs` | ✅ 已迁 `Modules/Identity/Infrastructure/Persistence/`（第 5 步） |
| `Controllers/Auth/AuthController.cs`、`UseCases/Identity/UserSyncService.cs` | `Modules/Identity/Api/` + `Modules/Identity/Application/` |
| `Models/DomainRuleException.cs`、`Services/Mhop/MhopJson.cs`、`Services/Mhop/MhopApiException{,.Filter}.cs`、`Controllers/Filters/*`（2）、`Services/Ai/*`（4） | `Shared/`（`Exceptions`、`Json`、`Filters`、`Ai`） |
| `Controllers/Misc/LongLinkController.cs`、`Controllers/SurvivalCraft/ServerController.cs` | `Modules/Link/` 与 `Modules/SurvivalCraft/`（现为独立小模块，不宜塞进上面的上下文） |
| `Data/ClouderyApiContext.cs`（含 Zhuxs*/Members/Exam* 表）、`Migrations/*`（23 个文件） | ✅ 按计划暂留原位（ClouderyApiContext 待 Stage 5 拆分；迁移文件的 `[DbContext]`/using 已在第 5 步同步） |

`MhopApiException`/`MhopApiExceptionFilter` 虽然名字带 Mhop，但过滤器在 `Program.cs:19` 全局注册且同样映射 `DomainRuleException`，属横切设施，放 `Shared/Exceptions` 更贴切；若移动，需同步 `Program.cs` 的 using。

## 迁移与 DbContext 的取舍（重要）

- 三个上下文都没有配置 `MigrationsAssembly` / `MigrationsHistoryTable`（`Program.cs:26/31/57` 的 `UseMySQL` 只传连接串），所以**三个上下文共享同一个 `__EFMigrationsHistory`**；迁移按「同程序集 + `[DbContext(typeof(X))]` 特性」发现。
- `[DbContext(...)]` 只出现在 `Migrations/**/*.Designer.cs` 与 `*ModelSnapshot.cs`：ClouderyApi 7 个文件、Mhop 13 个、Identity 3 个（合计 23）。**只要 DbContext 的命名空间或类名变化，就必须同步改这 23 个文件的 `using`**，否则 `dotnet ef` 会报找不到上下文/快照。
- 因此第一轮建议**保持 DbContext 留在 `ClouderyApi/Data/`**（方案文档已允许），只移动 Domain / Application / Api / Infrastructure；把「DbContext 迁入 `Modules/*/Infrastructure/Persistence`」作为独立的第 5 步单独提交，方便回滚。
- ✅ 实际执行即按此：第 5 步（`8aec30c`）同步了 Mhop 7 个（6 Designer + snapshot）与 Identity 2 个文件的 `using`，`ClouderyApiContext` 留在 `Data/`；迁移文件 BOM 与行尾逐字节保持。
- MVC 控制器发现依赖 `AddControllers()` 的程序集扫描，与命名空间无关，路由不会变；`Program.cs` 的 DI 注册只需跟随类型命名空间改 `using`。

## 执行顺序（每步独立提交、可编译、可回滚）

1. ✅ `Shared/` 抽取（DomainRuleException、MhopJson、MhopApiException + Filter、Filters、Ai）——触点少，为后续移动打底（`4d2ebd4`）。
2. ✅ Cloudery + Zhuxs（不涉及 DbContext）（`f1bdfd3`）。
3. ✅ Identity（AuthController 风险最高：Casdoor 回调 + Cookie 签发，纯移动，逻辑零改）（`e76e220`）。
4. ✅ Mhop，分三次：Domain → Application → Api/Infrastructure（`2c533a3` / `739fd23` / `786b7e6`）。
5. ✅ DbContext 迁入 Infrastructure + 同步迁移文件的 `using`（`8aec30c`；另附 `Controllers/Misc`、`Controllers/SurvivalCraft` 两个控制器进 `Modules/Link/Api` 与 `Modules/SurvivalCraft/Api`）。

每步验收：`dotnet build ClouderyApi.sln --nologo -v q` 0 error；`dotnet test ClouderyApi.Tests` 185 全绿（单跑，避免 3307 连接打满导致的假失败）；`dotnet ef migrations list --context <X>` 三个上下文均无异常；`grep` 旧命名空间 0 命中。

## 风险与注意

- 第 5 步的 23 个迁移文件是手改风险集中点，且迁移快照一旦命名空间错位，`dotnet ef` 的错误信息不直观。
- `MhopControllerBase` / `MhopAdminAttribute` / `MhopJson` 被多处引用，移动时逐个补 `using`。
- `Controllers/SurvivalCraft/ServerController.cs` 用自建 `ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")` 读配置（不读应用 IConfiguration），移动目录时**不要**改变该相对路径语义。
- Stage 3 已收尾（7 次提交，主树 `8aec30c`）。Stage 4（领域事件与事务边界）可启动，施工图见 docs/DDD-STAGE4-DOMAIN-EVENTS.md；Stage 5 施工图见 docs/DDD-STAGE5-CROSS-CUTTING.md。唯一遗留的第 11 项（`ConfigureApiBehaviorOptions` 统一 400 契约）属全局契约变更，单独立项，不与目录重组混合。