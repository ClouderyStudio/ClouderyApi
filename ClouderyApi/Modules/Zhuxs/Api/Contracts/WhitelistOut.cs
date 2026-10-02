namespace ClouderyApi.Modules.Zhuxs.Api.Contracts;

/// <summary>
/// 白名单（邀请码）读接口的输出视图。属性声明顺序必须与 Whitelist 实体一致
/// （id → code）。邀请码属敏感数据，仅管理员可读。
/// </summary>
public class WhitelistOut
{
    public required string Id { get; set; }
    public required string Code { get; set; }
}
