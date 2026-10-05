using System.Reflection;
using ClouderyApi.Modules.Cloudery.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;

namespace ClouderyApi.Tests;

/// <summary>
/// 架构门禁（NetArchTest）：把 Stage 3「按限界上下文重组目录」与 Stage 5「Cloudery/Zhuxs 持久化接口隔离」
/// 的约定固化成可执行断言。只约束依赖方向与层次归属，不涉及对外 HTTP 契约；
/// 任何让 Domain 反向依赖外层、让控制器重新摸到 DbContext、或让 Cloudery/Zhuxs 绕过
/// IClouderyDbContext / IZhuxsDbContext 直连 Persistence 的改动都会在这里失败。
///
/// 注意：选择类型一律用 <c>ResideInNamespaceStartingWith</c> 加完整命名空间
/// （不能用 <c>ResideInNamespaceContaining(".Api")</c> —— 根命名空间 ClouderyApi 自身就含 "Api"）。
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Api = typeof(ExamPaper).Assembly;

    private const string ModulesPrefix = "ClouderyApi.Modules.";

    private static readonly string[] Modules =
        ["Cloudery", "Zhuxs", "Identity", "Mhop", "Scforge", "Link", "SurvivalCraft"];

    private static readonly string[] LayerNames = ["Domain", "Application", "Api", "Infrastructure"];

    /// <summary>Modules/&lt;Ctx&gt;/&lt;Layer&gt;/ 对应的命名空间前缀（含其子命名空间）。</summary>
    private static string LayerNamespace(string module, string layer) => $"{ModulesPrefix}{module}.{layer}";

    private static Func<PredicateList> InNamespace(string ns) =>
        () => Types.InAssembly(Api).That().ResideInNamespaceStartingWith(ns);

    private static void AssertFreeOf(Func<PredicateList> select, string description, params string[] forbiddenPrefixes)
    {
        foreach (var prefix in forbiddenPrefixes)
        {
            var result = select().Should().NotHaveDependencyOn(prefix).GetResult();
            Assert.True(
                result.IsSuccessful,
                $"{description} 不得依赖 {prefix}；违规类型：{string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    /// <summary>门禁本身不得空跑：每个层次与 Shared 都必须真的选到类型，否则断言会退化成永远通过。</summary>
    [Fact]
    public void Gate_selections_are_not_vacuous()
    {
        foreach (var layer in LayerNames)
        {
            Assert.True(
                Modules.Any(m => InNamespace(LayerNamespace(m, layer))().GetTypes().Any()),
                $"没有任何模块存在 {layer} 层，该层门禁会空跑");
        }

        Assert.NotEmpty(InNamespace("ClouderyApi.Modules")().GetTypes());
        Assert.NotEmpty(InNamespace("ClouderyApi.Shared")().GetTypes());
    }

    /// <summary>Domain 是最内层：不得依赖任何模块的 Application / Api / Infrastructure。</summary>
    [Fact]
    public void Domain_must_not_depend_on_outer_layers()
    {
        var outerLayers = Modules
            .SelectMany(m => new[] { "Application", "Api", "Infrastructure" }.Select(l => LayerNamespace(m, l)))
            .ToArray();

        foreach (var module in Modules)
        {
            AssertFreeOf(InNamespace(LayerNamespace(module, "Domain")), $"{module}.Domain", outerLayers);
        }
    }

    /// <summary>Domain 也不得直接使用 EF Core 或 ASP.NET Core（保持可在无框架环境下单测）。</summary>
    [Fact]
    public void Domain_must_not_depend_on_ef_core_or_aspnet_core()
    {
        foreach (var module in Modules)
        {
            AssertFreeOf(
                InNamespace(LayerNamespace(module, "Domain")),
                $"{module}.Domain",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore");
        }
    }

    /// <summary>控制器层不得直接使用 EF Core（DbContext / IQueryable / SaveChanges 一律留在应用层与基础设施层）。</summary>
    [Fact]
    public void Api_layer_must_not_depend_on_ef_core()
    {
        foreach (var module in Modules)
        {
            AssertFreeOf(InNamespace(LayerNamespace(module, "Api")), $"{module}.Api", "Microsoft.EntityFrameworkCore");
        }
    }

    /// <summary>除 Api 层外，各层都不得依赖 MVC（应用层不感知 HTTP）。</summary>
    [Fact]
    public void Inner_layers_must_not_depend_on_mvc()
    {
        foreach (var module in Modules)
        {
            foreach (var layer in new[] { "Application", "Domain", "Infrastructure" })
            {
                AssertFreeOf(InNamespace(LayerNamespace(module, layer)), $"{module}.{layer}", "Microsoft.AspNetCore.Mvc");
            }
        }
    }

    /// <summary>Shared 是共享内核：只能被模块依赖，不能反向依赖 Modules。</summary>
    [Fact]
    public void Shared_must_not_depend_on_modules()
    {
        AssertFreeOf(InNamespace("ClouderyApi.Shared"), "Shared", "ClouderyApi.Modules");
    }

    /// <summary>
    /// Cloudery / Zhuxs 已按 Stage 5 §5.1 完成持久化接口隔离（只依赖 IClouderyDbContext / IZhuxsDbContext）：
    /// Api 与 Application 层一旦重新引用本模块的 Infrastructure，说明隔离被穿透。
    /// </summary>
    [Fact]
    public void Cloudery_and_Zhuxs_inner_layers_must_not_bypass_their_dbcontext_interfaces()
    {
        foreach (var module in new[] { "Cloudery", "Zhuxs" })
        {
            foreach (var layer in new[] { "Api", "Application" })
            {
                AssertFreeOf(
                    InNamespace(LayerNamespace(module, layer)),
                    $"{module}.{layer}",
                    LayerNamespace(module, "Infrastructure"));
            }
        }
    }

    /// <summary>控制器必须位于 Modules/&lt;Ctx&gt;/Api/ 且继承 ControllerBase（避免新的“散落控制器”）。</summary>
    [Fact]
    public void Controllers_must_live_in_module_api_namespace()
    {
        var controllers = Api.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(controllers);
        Assert.All(controllers, type =>
        {
            Assert.NotNull(type.Namespace);
            Assert.Matches(@"^ClouderyApi\.Modules\.[^.]+\.Api(\.|$)", type.Namespace);
            Assert.True(typeof(ControllerBase).IsAssignableFrom(type), $"{type.FullName} 应继承 ControllerBase");
        });
    }

    /// <summary>每个 DbContext 都应落在 Modules/&lt;Ctx&gt;/Infrastructure/Persistence/（迁移与装配的前提）。</summary>
    [Fact]
    public void DbContexts_must_live_in_module_persistence()
    {
        var contexts = Api.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(contexts);
        Assert.All(contexts, type =>
        {
            Assert.NotNull(type.Namespace);
            Assert.EndsWith(".Infrastructure.Persistence", type.Namespace);
        });
    }
}
