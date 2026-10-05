namespace ClouderyApi.Modules.Zhuxs.Domain;

public class Whitelist
{
    public required string Id { get; set; }
    public required string Code { get; set; }

    /// <summary>新增邀请码：主键由服务端生成，客户端不可指定（防 over-posting）。</summary>
    public static Whitelist Create(string code) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Code = code,
    };
}