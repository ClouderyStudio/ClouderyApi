using ClouderyApi.Modules.Zhuxs.Domain;
using ClouderyApi.Modules.Zhuxs.Api.Contracts;

namespace ClouderyApi.Modules.Zhuxs.Application.Mapping;

/// <summary>
/// Application → 对客输出视图。字段集合与顺序必须与历史“直接返回 Application 实体”的响应一致
/// （实体属性顺序：Id / Passed / SubmissionDate / Sharables），嵌套 Sharable 复用实体类型。
/// </summary>
internal static class ApplicationMapper
{
    public static ApplicationOut ToOut(ClouderyApi.Modules.Zhuxs.Domain.Application application) => new()
    {
        Id = application.Id,
        Passed = application.Passed,
        SubmissionDate = application.SubmissionDate,
        Sharables = application.Sharables,
    };
}
