# Stage 3 模块化单体：目录映射与执行要点

本文是 [docs/DDD-REFACTOR-PLAN.md](docs/DDD-REFACTOR-PLAN.md) Stage 3「按限界上下文重组目录」的施工图。**只做文件移动与命名空间调整，零行为变更**：HTTP 路由、JSON 契约、鉴权语义一律不变，每个子步骤都要独立构建 + 全量测试通过。

## 目标布局

```
ClouderyApi/
  Modules/
    Mhop/{Domain,Application,Infrastructure,Api}
    Cloudery/{Domain,Application,Infrastructure,Api}
    Zhuxs/{Domain,Application,Infrastructure,Api}
    Identity/{Domain,Infrastructure,Api}
  Shared/{Contracts,Json,Exceptions,Filters,Ai}
  Data/            # 可选保留：三个 DbContext
  Migrations/      # 保留原位（MigrationsNamespace 默认 = 根命名空间 + Migrations.<Context>）
  Program.cs
```

## 命名空间规则（关键）

- 模块根命名空间统一用 `ClouderyApi.Modules.<Context>.<Layer>`。
- **绝不能**使用 `ClouderyApi.Application`：Zhuxs 实体类型的短名就是 `Application`（`ClouderyApi.Models.Zhuxs.Application`），C# 向上查找未限定名时会在 `ClouderyApi` 层先命中命名空间 `ClouderyApi.Application`，从而报 CS0118（历史实测命中 ClouderyApi.Data/ClouderyApiContext.cs 与 ClouderyApi/Controllers/Zhuxs/ApplicationsController.cs）。`ClouderyApi.Modules.Zhuxs.Application` 不会触发该冲突，因为 `ClouderyApi` 层只有 `Modules`。
- 层目录名仍可用 `Application`（目录名不参与命名解析），但命名空间末端是 `...Zhuxs.Application`，位于 `Modules` 之下，安全。

## 现状普查（ClouderyApi/ 下 129 个 .cs、24 个命名空间）

| 文件数 | 命名空间 |
|---|---|
| 24 | ClouderyApi.Services.Mhop（Stage 2 后部分迁往 UseCases.Mhop） |
| 18 | ClouderyApi.Models.Mhop |
| 13 | ClouderyApi.Migrations.Mhop |
| 11 | ClouderyApi.Controllers.Mhop |
| 7 | ClouderyApi.Migrations.ClouderyApi |
| 7 | ClouderyApi.Models.Mhop.DTOs |
| 5 | ClouderyApi.Data |
| 4 | ClouderyApi.Controllers.Cloudery |
| 4 | ClouderyApi.Services.Ai |
| 3 | ClouderyApi.Controllers.Zhuxs / Models.Zhuxs / Models.Zhuxs.DTOs / Migrations.Identity / Models.Cloudery / Models.Cloudery.DTOs |
| 2 | ClouderyApi.UseCases.Mhop / Controllers.Filters / Services.Cloudery |
| 1 | ClouderyApi.Controllers.{Auth,SurvivalCraft,Misc} / UseCases.Mhop.Mapping / Models / Models.Identity |

## 映射表

### Mhop

| 现状 | 目标 | 依据 |
|---|---|---|
| `Models/Mhop/*.cs`（18，不含 DTOs） | `Modules/Mhop/Domain/` | 聚合、值对象、纯规则 |
| `Services/Mhop/MhopContentPolicy.cs`、`MhopModeration.cs` | `Modules/Mhop/Domain/` | 纯策略/危机识别，无 IO |
| `UseCases/Mhop/{AssessmentAppService,ForumAppService}.cs`、`UseCases/Mhop/Mapping/*`、`Services/Mhop/MhopAdminPermissions.cs`、`MhopBottleService.cs`（→ `BottleAppService`）、`MhopContentService.cs`、`MhopContentReviewService.cs`、`MhopUploadService.cs`、`MhopBottleMapper.cs`（→ `Application/Mapping/`） | `Modules/Mhop/Application/` | 用例编排 |
| `Controllers/Mhop/*`（11）、`Models/Mhop/DTOs/*`（7）、`Services/Mhop/{MhopJson,MhopApiException,MhopApiExceptionFilter}.cs`、`Controllers/Mhop/{MhopControllerBase,MhopAdminAttribute}.cs` | `Modules/Mhop/Api/`（DTO 可放 `Api/Contracts/`） | HTTP 契约与序列化 |
| `Services/Mhop/{MhopAiService,MhopBottleTimeoutService,MhopCasdoorService,MhopCurrentUserAccessor,MhopEmailCodeService,MhopJwtService,MhopObjectStorage,MhopOnlineTracker,MhopOptions,MhopOrphanSweeper,MhopPasswordHasher,MhopSeeder,MhopSmtpClient}.cs` | `Modules/Mhop/Infrastructure/` | 出站依赖与持久化辅助 |
| `Data/MhopDbContext.cs`、`MhopDbContextFactory.cs` | 暂留 `Data/`（可选迁 `Modules/Mhop/Infrastructure/Persistence/`） | 见下节取舍 |

### Cloudery / Zhuxs / Identity / Shared

| 现状 | 目标 |
|---|---|
| `Models/Cloudery/{ExamPaper,ExamResult,Member}.cs` + `DTOs/*`（3） | `Modules/Cloudery/Domain/` + `Api/Contracts/` |
| `Services/Cloudery/{ExamResultService,ResultAnalysisService}.cs` | `Modules/Cloudery/Application/` |
| `Controllers/Cloudery/*`（4） | `Modules/Cloudery/Api/` |
| `Models/Zhuxs/{Application,Term,Whitelist}.cs` + `DTOs/*`（3） | `Modules/Zhuxs/Domain/` + `Api/Contracts/` |
| `Controllers/Zhuxs/*`（3） | `Modules/Zhuxs/Api/`（该模块目前无服务层） |
| `Models/Identity/User.cs` | `Modules/Identity/Domain/` |
| `Data/IdentityDbContext.cs` + `IdentityDbContextFactory.cs` | `Modules/Identity/Infrastructure/`（或暂留 Data/） |
| `Controllers/Auth/AuthController.cs` | `Modules/Identity/Api/` |
| `Models/DomainRuleException.cs`、`Services/Mhop/MhopJson.cs`、`Services/Mhop/MhopApiException{,.Filter}.cs`、`Controllers/Filters/*`（2）、`Services/Ai/*`（4） | `Shared/`（`Exceptions`、`Json`、`Filters`、`Ai`） |
| `Controllers/Misc/LongLinkController.cs`、`Controllers/SurvivalCraft/ServerController.cs` | `Modules/Link/` 与 `Modules/SurvivalCraft/`（现为独立小模块，不宜塞进上面的上下文） |
| `Data/ClouderyApiContext.cs`（含 Zhuxs*/Members/Exam* 表）、`Migrations/*`（23 个文件） | 暂留原位 |

`MhopApiException`/`MhopApiExceptionFilter` 虽然名字带 Mhop，但过滤器在 `Program.cs:19` 全局注册且同样映射 `DomainRuleException`，属横切设施，放 `Shared/Exceptions` 更贴切；若移动，需同步 `Program.cs` 的 using。

## 迁移与 DbContext 的取舍（重要）

- 三个上下文都没有配置 `MigrationsAssembly` / `MigrationsHistoryTable`（`Program.cs:26/31/57` 的 `UseMySQL` 只传连接串），所以**三个上下文共享同一个 `__EFMigrationsHistory`**；迁移按「同程序集 + `[DbContext(typeof(X))]` 特性」发现。
- `[DbContext(...)]` 只出现在 `Migrations/**/*.Designer.cs` 与 `*ModelSnapshot.cs`：ClouderyApi 7 个文件、Mhop 13 个、Identity 3 个（合计 23）。**只要 DbContext 的命名空间或类名变化，就必须同步改这 23 个文件的 `using`**，否则 `dotnet ef` 会报找不到上下文/快照。
- 因此第一轮建议**保持 DbContext 留在 `ClouderyApi/Data/`**（方案文档已允许），只移动 Domain / Application / Api / Infrastructure；把「DbContext 迁入 `Modules/*/Infrastructure/Persistence`」作为独立的第 5 步单独提交，方便回滚。
- MVC 控制器发现依赖 `AddControllers()` 的程序集扫描，与命名空间无关，路由不会变；`Program.cs` 的 DI 注册只需跟随类型命名空间改 `using`。

## 执行顺序（每步独立提交、可编译、可回滚）

1. `Shared/` 抽取（DomainRuleException、MhopJson、MhopApiException + Filter、Filters、Ai）——触点少，为后续移动打底。
2. Cloudery + Zhuxs（不涉及 DbContext）。
3. Identity（AuthController 风险最高：Casdoor 回调 + Cookie 签发，纯移动，逻辑零改）。
4. Mhop，分三次：Domain → Application → Api/Infrastructure。
5. （可选）DbContext 迁入 Infrastructure + 同步 23 个迁移文件的 `using`。

每步验收：`dotnet build ClouderyApi.sln --nologo -v q` 0 error；`dotnet test ClouderyApi.Tests` 145 全绿；`dotnet ef migrations list --context <X>` 三个上下文均无异常；`grep` 旧命名空间 0 命中。

## 风险与注意

- 第 5 步的 23 个迁移文件是手改风险集中点，且迁移快照一旦命名空间错位，`dotnet ef` 的错误信息不直观。
- `MhopControllerBase` / `MhopAdminAttribute` / `MhopJson` 被多处引用，移动时逐个补 `using`。
- `Controllers/SurvivalCraft/ServerController.cs` 用自建 `ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")` 读配置（不读应用 IConfiguration），移动目录时**不要**改变该相对路径语义。
- Stage 2 仍在进行（MHOP 应用层抽取未完成），Stage 3 待 Stage 2 收尾后再启动，避免同一目录反复改名。