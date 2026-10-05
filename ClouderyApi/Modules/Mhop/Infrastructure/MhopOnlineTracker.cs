using ClouderyApi.Shared.Online;

namespace ClouderyApi.Modules.Mhop.Infrastructure;

/// <summary>
/// 在线人数：心跳 + 滑动窗口计数。真正的状态在 <see cref="IOnlineTrackerStore"/> 里
/// （未配 Redis 时是进程内实现，配了就是跨实例共享的 Redis ZSET）；
/// 本类只固定窗口长度并转调，调用点不必关心用的是哪种存储。
/// </summary>
public sealed class MhopOnlineTracker
{
    /// <summary>心跳窗口：超过这么久没有心跳就视为离线。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(90);

    private readonly IOnlineTrackerStore _store;

    public MhopOnlineTracker(IOnlineTrackerStore store) => _store = store;

    public ValueTask<int> HeartbeatAsync(string key) => _store.HeartbeatAsync(key, Window);

    public ValueTask<int> CountAsync() => _store.CountAsync(Window);
}
