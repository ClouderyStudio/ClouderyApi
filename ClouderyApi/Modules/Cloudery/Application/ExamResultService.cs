using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Modules.Cloudery.Domain;
using ClouderyApi.Modules.Cloudery.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Cloudery.Application;

/// <summary>上传的某条结果不被接受（内容为空、单条过大、批量过大）。控制器据此返回 400 裸对象。</summary>
public sealed class ExamResultRejectedException(string message) : Exception(message);

/// <summary>
/// 登录用户的云端测评结果。结果正文整体存 JSON，服务端只负责归属、幂等、排序与配额，
/// 不解析站点存档的内部结构 —— 站点加字段不需要改这里。
/// </summary>
public sealed class ExamResultService(IClouderyDbContext db)
{
    /// <summary>每个用户最多保留的记录数（与站点本机存档的 MAX_TOTAL 对齐）</summary>
    public const int MaxRecordsPerUser = 200;

    /// <summary>单次同步最多接受的记录数，挡住把整个存档反复灌库的请求</summary>
    public const int MaxRecordsPerSync = 200;

    /// <summary>单条结果正文的字符上限（约 256 KB）</summary>
    public const int MaxPayloadChars = 256 * 1024;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>该用户的全部云端记录（新的在前）</summary>
    public async Task<List<ExamResultOut>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await db.ExamResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken);

        return Sort(rows).Select(ToOut).ToList();
    }

    /// <summary>
    /// 把一批本机记录并入云端，并返回该用户云端的全量记录：
    /// 客户端据此把其它设备上的记录补进本机存档（多平台共享就是这么实现的）。
    /// </summary>
    public async Task<ExamResultSyncOut> SyncAsync(
        Guid userId,
        IReadOnlyList<ExamResultIn> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count > MaxRecordsPerSync)
        {
            throw new ExamResultRejectedException($"一次最多同步 {MaxRecordsPerSync} 条记录，请分批上传");
        }

        var now = DateTime.UtcNow;
        var rows = await db.ExamResults.Where(r => r.UserId == userId).ToListAsync(cancellationToken);
        var uploaded = 0;

        foreach (var record in records)
        {
            var testId = NormalizeTestId(record.TestId);
            if (testId.Length == 0) continue;

            var payload = SerializePayload(record.Payload);
            var savedAt = NormalizeTime(record.SavedAt) ?? now;
            var clientKey = NormalizeClientKey(record.ClientKey, testId, savedAt);
            var title = Truncate(record.TestTitle, 200);

            var existing = rows.FirstOrDefault(r => r.ClientKey != null && r.ClientKey == clientKey);
            if (existing is null)
            {
                var row = new ExamResult
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = userId,
                    TestId = testId,
                    TestTitle = title,
                    ClientKey = clientKey,
                    SavedAt = savedAt,
                    Payload = payload,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.ExamResults.Add(row);
                rows.Add(row);
                uploaded++;
                continue;
            }

            // 同一条记录重复上传时只在"内容确实更新过"的情况下覆盖，
            // 避免旧设备把新设备写入的内容顶回去（SavedAt 回落即为旧）。
            if (existing.SavedAt is null || savedAt >= existing.SavedAt.Value)
            {
                existing.TestId = testId;
                existing.TestTitle = title;
                existing.SavedAt = savedAt;
                existing.Payload = payload;
                existing.UpdatedAt = now;
                uploaded++;
            }
        }

        Prune(rows, now);
        await db.SaveChangesAsync(cancellationToken);

        var all = Sort(rows).Select(ToOut).ToList();
        return new ExamResultSyncOut
        {
            Success = true,
            Uploaded = uploaded,
            Total = all.Count,
            Results = all,
        };
    }

    /// <summary>删除一条记录（按云端 Id 或站点本机记录键）</summary>
    public async Task<bool> DeleteAsync(Guid userId, string idOrClientKey, CancellationToken cancellationToken = default)
    {
        var key = (idOrClientKey ?? string.Empty).Trim();
        if (key.Length == 0) return false;

        var row = await db.ExamResults.FirstOrDefaultAsync(
            r => r.UserId == userId && (r.Id == key || r.ClientKey == key),
            cancellationToken);
        if (row is null) return false;

        db.ExamResults.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>清空该用户的全部云端记录，返回删除条数</summary>
    public async Task<int> ClearAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await db.ExamResults.Where(r => r.UserId == userId).ToListAsync(cancellationToken);
        if (rows.Count == 0) return 0;

        db.ExamResults.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    /// <summary>超出配额的旧记录直接删除（与站点本机存档的裁剪口径一致：只留最新 200 条）</summary>
    private void Prune(List<ExamResult> rows, DateTime now)
    {
        var ordered = Sort(rows).ToList();
        if (ordered.Count <= MaxRecordsPerUser) return;

        foreach (var row in ordered.Skip(MaxRecordsPerUser))
        {
            if (row.UpdatedAt == default) row.UpdatedAt = now;
            db.ExamResults.Remove(row);
            rows.Remove(row);
        }
    }

    private static IEnumerable<ExamResult> Sort(IEnumerable<ExamResult> rows) =>
        rows.OrderByDescending(r => r.SavedAt ?? r.UpdatedAt).ThenByDescending(r => r.UpdatedAt);

    private static ExamResultOut ToOut(ExamResult row) => new()
    {
        Id = row.Id,
        ClientKey = row.ClientKey,
        TestId = row.TestId,
        TestTitle = row.TestTitle,
        SavedAt = row.SavedAt,
        UpdatedAt = row.UpdatedAt,
        Payload = ParsePayload(row.Payload),
    };

    private static JsonElement? ParsePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            // 必须在释放 JsonDocument 之前复制，否则取到的是已释放的内存
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SerializePayload(JsonElement? payload)
    {
        if (payload is null) throw new ExamResultRejectedException("记录缺少结果正文（payload）");

        var json = payload.Value.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : JsonSerializer.Serialize(payload.Value, PayloadJsonOptions);
        if (json.Length > MaxPayloadChars)
        {
            throw new ExamResultRejectedException("单条结果过大，无法上传到云端");
        }
        return json;
    }

    private static string NormalizeTestId(string? testId) => Truncate(testId, 64)?.Trim() ?? string.Empty;

    /// <summary>缺少本机记录键时按量表 + 存档时间生成，保证同一份结果重复上传仍然幂等</summary>
    private static string NormalizeClientKey(string? clientKey, string testId, DateTime savedAt)
    {
        var key = Truncate(clientKey, 120)?.Trim();
        if (!string.IsNullOrEmpty(key)) return key;

        return Truncate(testId + "@" + savedAt.ToString("O"), 120) ?? testId;
    }

    /// <summary>统一成 UTC 后入库：客户端可能送来带偏移或没有时区的时间，MySQL 的 datetime 不接受本地时间标记</summary>
    private static DateTime? NormalizeTime(DateTime? value)
    {
        if (value is null) return null;
        var v = value.Value;
        var utc = v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v;
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length > max ? value[..max] : value;
    }
}
