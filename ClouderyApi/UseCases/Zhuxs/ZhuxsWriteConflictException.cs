namespace ClouderyApi.UseCases.Zhuxs;

/// <summary>
/// Zhuxs 用例的写冲突（唯一键冲突 / 保存失败）。控制器捕获后返回 409 与模块自己的中文文案。
/// 刻意不复用 MHOP 的 DomainRuleException：全局 MhopApiExceptionFilter 会把它渲染成 MHOP 形状的 {detail} 响应。
/// </summary>
public sealed class ZhuxsWriteConflictException : Exception
{
    public ZhuxsWriteConflictException()
    {
    }

    public ZhuxsWriteConflictException(string message) : base(message)
    {
    }

    public ZhuxsWriteConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
