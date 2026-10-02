namespace ClouderyApi.UseCases.Cloudery;

/// <summary>
/// Cloudery 用例的写冲突（唯一键冲突 / 保存失败）。控制器捕获后返回 409 与模块自己的中文文案。
/// 刻意不复用 MHOP 的 DomainRuleException：全局 MhopApiExceptionFilter 会把它渲染成 MHOP 形状的 {detail} 响应。
/// </summary>
public sealed class ClouderyWriteConflictException : Exception
{
    public ClouderyWriteConflictException()
    {
    }

    public ClouderyWriteConflictException(string message) : base(message)
    {
    }

    public ClouderyWriteConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
