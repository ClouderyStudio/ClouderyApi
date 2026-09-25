namespace ClouderyApi.Services.Mhop;

/// <summary>业务异常：由 MhopApiExceptionFilter 转换为 { detail } 响应，对应 Python 的 HTTPException。</summary>
public sealed class MhopApiException : Exception
{
    public int StatusCode { get; }

    public string Detail { get; }

    public MhopApiException(int statusCode, string detail) : base(detail)
    {
        StatusCode = statusCode;
        Detail = detail;
    }
}
