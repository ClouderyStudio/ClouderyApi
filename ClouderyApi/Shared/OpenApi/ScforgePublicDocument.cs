using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ClouderyApi.Shared.OpenApi;

/// <summary>
/// SCForge 对外开放 API 的 OpenAPI 文档定义：Swashbuckle 文档名 <see cref="Name"/>，
/// 只收录 /scforge 下对第三方开放的非 admin 端点，并声明 scf_ API Key 安全方案。
///
/// 对外文档站（ClouderyDoc 的 /api 分区）与入库产物 docs/openapi/scforge-public.json
/// 都以这份文档为唯一事实来源：产物由 ClouderyApi.Tests/ScforgePublicOpenApiTests.cs
/// 从运行时 swagger.json 导出并守护漂移，不允许手改。
/// </summary>
public static class ScforgePublicDocument
{
    /// <summary>Swashbuckle 文档名，对应 URL /swagger/scforge-public/swagger.json。</summary>
    public const string Name = "scforge-public";

    /// <summary>文档标题（Swagger UI 与文档站的展示名）。</summary>
    public const string Title = "SCForge 开放 API";

    /// <summary>文档版本号。</summary>
    public const string Version = "1.0.0";

    /// <summary>API Key 安全方案在 components.securitySchemes 里的键名。</summary>
    public const string SecuritySchemeName = "ScforgeApiKey";

    /// <summary>对外开放的模块路由前缀（不含前导斜杠）。</summary>
    public const string ModulePrefix = "scforge";

    /// <summary>不对外开放的后台路由前缀（不含前导斜杠）。</summary>
    public const string AdminPrefix = "scforge/admin";

    /// <summary>公开面使用的 API Key 令牌前缀（与 ScforgeApiKeyScopes.TokenPrefix 同口径）。</summary>
    public const string TokenPrefix = "scf_";

    /// <summary>对外文档声明的 API 入口（Swagger UI 的 Try it out 与文档站代码样例都用它）。</summary>
    public const string PublicBaseUrl = "https://api.cldery.com";

    /// <summary>对外文档的元信息。</summary>
    public static OpenApiInfo Info() => new()
    {
        Version = Version,
        Title = Title,
        Description =
            "SCForge（生存战争插件 / 模组资源平台）的对外开放接口。\n\n" +
            "**鉴权**：读接口匿名可用；与「我的资源」相关的读写需要 API Key —— " +
            "请求头 Authorization: Bearer scf_<base64url>。签发与作用域见接入指南。\n\n" +
            "**错误形状**：所有错误响应统一为 { \"detail\": \"中文说明\" }（见 ApiError.cs），" +
            "HTTP 状态码即语义（400 参数或规则不合法、401 未登录、403 作用域不足、404 资源不存在、409 状态冲突）。\n\n" +
            "**时间**：所有时间字段按北京时间（UTC+8，+08:00）输出。"
    };

    /// <summary>
    /// 判断某个端点的 ApiDescription.RelativePath（无前导斜杠）是否进入对外文档：
    /// /scforge 之下、且不属于 /scforge/admin 后台面。
    /// </summary>
    public static bool Includes(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var path = relativePath.TrimStart('/');
        if (path.StartsWith(AdminPrefix + "/", StringComparison.OrdinalIgnoreCase)) return false;

        return path.StartsWith(ModulePrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把对外文档注册进 SwaggerGen（文档、路径过滤、API Key 安全方案、XML 注释）。</summary>
    public static void Apply(SwaggerGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.SwaggerDoc(Name, Info());

        // v1 保持全量（含 admin，仅供内部开发看）；对外文档按 Includes 过滤
        options.DocInclusionPredicate((documentName, api) =>
            !string.Equals(documentName, Name, StringComparison.Ordinal)
            || Includes(api.RelativePath));

        // API Key 走标准 Bearer 头，令牌自带 scf_ 前缀便于在日志 / 网关侧识别
        options.AddSecurityDefinition(SecuritySchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = TokenPrefix + "<base64url>",
            Description =
                "SCForge API Key（scf_ 开头）。读接口匿名也可调用，带上 Key 才能访问「我的资源」与写接口。"
        });

        // 放在文档级：公开面里多数读端点匿名可用，带 Key 只是获得身份与作用域，
        // 具体哪些端点强制鉴权以各端点的 401 / 403 说明为准。
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = new List<string>()
        });

        // 公开面的服务器入口。Swashbuckle 的 servers 挂在生成器上、不分文档，
        // 所以 v1 会一并带上 —— 两个文档本就指向同一台 API，符合预期
        // （ClouderyApi.Tests/SwaggerRouteSnapshotTests.cs 只比对路由表，不受影响）。
        options.AddServer(new OpenApiServer
        {
            Url = PublicBaseUrl,
            Description = "生产环境"
        });

        // 控制器 / DTO 的中文 XML 注释随程序集一起产出；测试宿主里同样位于输出目录
        var xmlPath = Path.Combine(AppContext.BaseDirectory, "ClouderyApi.xml");
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }

        // 换行归一：让产物与 Linux CI 逐字节一致（见过滤器注释）
        options.DocumentFilter<NewlineNormalizingDocumentFilter>();
    }

    /// <summary>
    /// 把对外文档里所有文本的换行统一成 \n。
    ///
    /// 起因：Swashbuckle 在 humanize 多行 XML 注释时用 Environment.NewLine 拼行，
    /// 同一份注释在 Windows 上生成 "\r\n"、在 Linux 上生成 "\n"，
    /// 于是入库产物（Windows 生成）在 CI（Linux）上与运行时文档不一致，
    /// ScforgePublicOpenApiTests.Public_document_matches_committed_artifact 会失败。
    /// 文档过滤器在所有生成器之后运行，这里做最后一道归一，两个平台就能产出同一份 JSON。
    /// </summary>
    private sealed class NewlineNormalizingDocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            // v1 全量文档（含 admin，仅供内部开发看）保持原样，只归一对外开放文档
            if (!string.Equals(swaggerDoc.Info?.Title, Title, StringComparison.Ordinal)) return;

            Normalize(swaggerDoc, new HashSet<object?>(ReferenceEqualityComparer.Instance));
        }

        /// <summary>递归遍历 OpenAPI 对象图，把字符串属性里的 \r\n / \r 归一成 \n。</summary>
        private static void Normalize(object? node, HashSet<object?> visited)
        {
            if (node is null or string || node.GetType().IsValueType) return;
            if (node.GetType().Namespace?.StartsWith("Microsoft.OpenApi", StringComparison.Ordinal) != true) return;
            if (!visited.Add(node)) return;

            foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0) continue;

                var value = property.GetValue(node);
                if (value is string text)
                {
                    if (property.CanWrite && text.Contains('\r'))
                    {
                        property.SetValue(node, text.Replace("\r\n", "\n").Replace('\r', '\n'));
                    }

                    continue;
                }

                // OpenAPI 模型里的集合都是 Dictionary / List；键（路径、schema 名、方案名）不含换行，只递归值
                if (value is System.Collections.IDictionary dictionary)
                {
                    foreach (var item in dictionary.Values) Normalize(item, visited);
                }
                else if (value is System.Collections.IEnumerable sequence)
                {
                    foreach (var item in sequence) Normalize(item, visited);
                }
                else
                {
                    Normalize(value, visited);
                }
            }
        }
    }
}
