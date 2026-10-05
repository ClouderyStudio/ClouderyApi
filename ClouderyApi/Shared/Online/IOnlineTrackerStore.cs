namespace ClouderyApi.Shared.Online;

/// <summary>
/// 在线人数的存储抽象：心跳打点 + 滑动窗口内的去重计数。
/// 两种实现（进程内 / Redis）语义一致——窗口内同一个 key 只算一个人，
/// 超过窗口没有心跳即自动消失。
/// </summary>
public interface IOnlineTrackerStore
{
    /// <summary>登记一次心跳，返回当前窗口内的在线人数。</summary>
    ValueTask<int> HeartbeatAsync(string key, TimeSpan window);

    /// <summary>读取当前窗口内的在线人数（不登记心跳）。</summary>
    ValueTask<int> CountAsync(TimeSpan window);
}
