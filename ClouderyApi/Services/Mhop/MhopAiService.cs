using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// AI 服务适配层（从 Python 后端 ai.py 迁移）。
/// 配置了 Llm:BaseUrl + Llm:ApiKey 时走 OpenAI 兼容 Chat Completions；
/// 未配置或调用失败时降级为内置共情式规则回复，保证平台离线/零成本仍可用。
/// 任何引擎下，只要检测到自伤/自杀危机，都强制在回复开头插入援助热线。
/// </summary>
public sealed class MhopAiService
{
    private const string CrisisPrefix =
        "我注意到你正经历非常痛苦、甚至可能想伤害自己的时刻，我很担心你的安全。" +
        "请不要独自硬撑：\n" +
        "1）立即拨打全国心理援助热线 12356（24小时、免费、保密），" +
        "或北京心理危机研究与干预中心 010-82951332；\n" +
        "2）如果你觉得自己可能马上做出伤害自己的事，请立刻拨打 110 或 120，" +
        "或直接前往最近医院的急诊；\n" +
        "3）现在就联系一位你信任的家人或朋友，告诉他/她你的感受，并尽量和人待在一起，远离危险物品。\n\n";

    private const string ForumSystemPrompt =
        "你是全国性公益心理辅助平台的心理支持助手。要求：\n" +
        "1. 语气温和、共情、不评判，先接纳情绪，再给出1-3条具体、可执行的适应性调节建议；\n" +
        "2. 不做医学诊断，不使用空洞说教，不承诺'一切都会好起来'；\n" +
        "3. 回复控制在300字以内；\n" +
        "4. 只要用户表达了自伤、自杀、不想活等危机信号，必须在回复最前面用明确文字" +
        "给出全国心理援助热线12356、北京心理危机研究与干预中心010-82951332，" +
        "并建议立即联系身边人或拨打110/120；\n" +
        "5. 不讨论任何伤害自己的具体方法。";

    private static readonly string[] FallbackOpenings =
    [
        "谢谢你愿意把这些说出来，能表达出来本身就需要勇气。",
        "我能感受到你现在的疲惫和不容易，你的感受是真实的，也值得被认真对待。",
        "隔着屏幕，我想先给你一个稳稳的陪伴——你不用一个人扛着这些。",
    ];

    private static readonly string[] FallbackTips =
    [
        "先做一个'着陆'练习：慢慢说出你眼前看到的5样东西、听到的4种声音、身体接触到的3样物品，把注意力拉回当下。",
        "尝试4-7-8呼吸：吸气4秒、屏息7秒、缓慢呼气8秒，重复3-4轮，帮助神经系统先安定下来。",
        "把脑子里的想法不加评判地写在纸上，区分'我感受到的情绪'和'我需要解决的问题'，前者需要被看见，后者可以拆成最小的一步。",
        "今天只给自己定一个最小目标（比如按时吃一顿饭、出门走10分钟），完成它就是有效的照顾。",
        "如果情绪在夜晚特别重，可以提前给自己安排一个'情绪急救包'：温热的饮品、能联系的人、一段让你安心的音频。",
        "情绪像海浪，峰值通常会在20-40分钟后回落。在最强烈的时候，先不做任何决定，只让自己安全地度过这一波。",
    ];

    private const string FallbackEnding =
        "\n\n这里是匿名、安全的空间，你可以继续多说一点。" +
        "如果这些困扰已经持续两周以上、明显影响睡眠、饮食或日常功能，建议到正规医院心理科/精神科做一次评估——" +
        "寻求专业帮助是力量，而不是软弱。";

    /// <summary>
    /// 审核提示词。刻意写得中性：服务商的内容策略会拦截「提示词里枚举敏感类目」的请求并直接返回 5xx，
    /// 一旦命中，整条审核链路都会退化成「AI 不可用」。具体类目判定交给规则预筛（MhopContentPolicy），
    /// 这里只让模型做「是否适合公开展示」的语义判断。
    /// </summary>
    private const string ModerationBaseRules =
        "你是社区内容安全审核员。判断下面这条用户文本是否适合在公开的心理互助社区发布，只输出一行，不要解释、不要引号：\n" +
        "safe\n" +
        "suspect|理由\n" +
        "violation|理由\n" +
        "规则：safe 表示正常表达、倾诉或求助；suspect 表示需要人工复核；violation 表示明显违法或严重不当、绝不适合公开展示。\n" +
        "理由不超过30字，写给人审看。拿不准就输出 suspect。";

    /// <summary>按内容类型拼接审核提示词：不同场景的风险点不同。</summary>
    private static string ModerationSystemPrompt(string kind) => kind switch
    {
        MhopContentKind.Bottle => ModerationBaseRules +
            "\n本次场景：陌生人漂流瓶的开场内容，将展示给一名随机陌生人。请特别留意营销推广、索要联系方式、诱导线下接触或金钱往来。",
        MhopContentKind.Message => ModerationBaseRules +
            "\n本次场景：匿名一对一聊天中的一条消息，收件人是同样的陌生人。请特别留意骚扰辱骂、索要联系方式、诱导转账或线下邀约。",
        _ => ModerationBaseRules +
            "\n本次场景：论坛公开内容，所有人可见。请特别留意广告营销、人身攻击、隐私泄露。",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MhopOptions _options;
    private readonly ILogger<MhopAiService> _logger;

    public MhopAiService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<MhopOptions> options,
        ILogger<MhopAiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>调用 OpenAI 兼容接口；返回 (回复文本, 引擎标识 llm/local)。</summary>
    public async Task<(string Text, string Engine)> ChatAsync(
        IReadOnlyList<Dictionary<string, string>> messages,
        CancellationToken cancellationToken = default,
        double temperature = 0.7,
        int maxTokens = 700)
    {
        var llm = _options.Llm;
        if (!string.IsNullOrWhiteSpace(llm.BaseUrl) && !string.IsNullOrWhiteSpace(llm.ApiKey))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, llm.BaseUrl.TrimEnd('/') + "/chat/completions");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", llm.ApiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        model = llm.Model,
                        messages,
                        temperature,
                        max_tokens = maxTokens,
                    }),
                    Encoding.UTF8,
                    "application/json");

                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(TimeSpan.FromSeconds(30));

                using var response = await client.SendAsync(request, timeoutSource.Token);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeoutSource.Token));
                var text = document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                if (!string.IsNullOrWhiteSpace(text)) return (text.Trim(), "llm");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM 调用失败，降级为本地共情引擎");
            }
        }
        return (string.Empty, "local");
    }

    /// <summary>
    /// AI 文本审核：先本地规则预筛，再做语义判断，返回结论 + 理由。
    /// safe（通过，可自动公开）/ suspect（疑似，转人工）/ violation（违规，转人工）/
    /// unavailable（LLM 未配置或调用失败，无法判断，转人工）。
    /// </summary>
    /// <param name="kind">见 <see cref="MhopContentKind"/>，决定提示词与规则口径。</param>
    public async Task<MhopModerationOutcome> ModerateAsync(
        string text, string kind = MhopContentKind.Post, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new MhopModerationOutcome(MhopModerationOutcome.Safe, string.Empty);

        // 规则预筛：确定性结论优先，命中即短路——不让大模型有机会把明显违规「翻案」成安全
        var rule = MhopContentPolicy.Screen(text);
        switch (rule.Verdict)
        {
            case MhopPolicyVerdict.Violation:
                return new MhopModerationOutcome(MhopModerationOutcome.Violation, rule.Reason);
            case MhopPolicyVerdict.Suspect:
                return new MhopModerationOutcome(MhopModerationOutcome.Suspect, rule.Reason);
            case MhopPolicyVerdict.Crisis:
                // 危机内容必须第一时间放行并展示援助信息：模型服务商对自伤类内容普遍直接拒答（5xx），
                // 交给它只会得到「不可用」，反而把最需要帮助的人卡在人工队列里。
                return new MhopModerationOutcome(MhopModerationOutcome.Safe, string.Empty);
        }

        var messages = new List<Dictionary<string, string>>
        {
            new() { ["role"] = "system", ["content"] = ModerationSystemPrompt(kind) },
            new() { ["role"] = "user", ["content"] = text.Length > 1000 ? text[..1000] : text },
        };

        // 审核要确定性输出：低温、短输出；调用/解析失败重试一次，仍失败才判「不可用」
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (reply, _) = await ChatAsync(messages, cancellationToken, temperature: 0.1, maxTokens: 120);
            if (!string.IsNullOrWhiteSpace(reply))
            {
                var outcome = ParseModeration(reply);
                if (outcome.Verdict != MhopModerationOutcome.Unavailable) return outcome;
            }
            if (attempt == 0) await Task.Delay(400, cancellationToken);
        }

        return new MhopModerationOutcome(MhopModerationOutcome.Unavailable, string.Empty);
    }

    /// <summary>解析审核模型输出（容忍 "label|理由" / "label: 理由" / markdown 包裹）。</summary>
    private static MhopModerationOutcome ParseModeration(string reply)
    {
        var line = reply.Trim();
        var breakAt = line.IndexOfAny(['\r', '\n']);
        if (breakAt >= 0) line = line[..breakAt];
        line = line.Trim().Trim('`', '"', '\'', '。', '.', ' ', '：', ':');

        string? reason = null;
        foreach (var separator in new[] { "|", "｜", ":", "：" })
        {
            var index = line.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0) continue;
            reason = line[(index + 1)..].Trim();
            line = line[..index].Trim();
            break;
        }

        var label = line.ToLowerInvariant();
        var verdict = label.StartsWith("violation", StringComparison.Ordinal) ? MhopModerationOutcome.Violation
            : label.StartsWith("suspect", StringComparison.Ordinal) ? MhopModerationOutcome.Suspect
            : label.StartsWith("safe", StringComparison.Ordinal) ? MhopModerationOutcome.Safe
            : MhopModerationOutcome.Unavailable;

        if (verdict == MhopModerationOutcome.Safe)
            return new MhopModerationOutcome(verdict, string.Empty);
        reason = string.IsNullOrWhiteSpace(reason) ? "AI 判定需要人工复核" : Truncate(reason, 200);
        return new MhopModerationOutcome(verdict, reason);
    }

    public async Task<(string Text, string Engine)> ForumReplyAsync(string userContent, bool crisis)
    {
        var (reply, engine) = await ChatAsync(
        [
            new Dictionary<string, string> { ["role"] = "system", ["content"] = ForumSystemPrompt },
            new Dictionary<string, string> { ["role"] = "user", ["content"] = userContent },
        ]);

        if (engine == "llm")
        {
            // 双保险：模型漏掉危机提示时本地强制补齐
            if (crisis && !reply.Contains("12356", StringComparison.Ordinal)) reply = CrisisPrefix + reply;
            return (reply, engine);
        }

        // ---- 本地兜底引擎（确定性伪随机，保证同一内容得到稳定回复）----
        var seed = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(userContent)), 0);
        var rng = new Random(seed);
        var parts = new List<string>();
        if (crisis) parts.Add(CrisisPrefix);
        parts.Add(FallbackOpenings[rng.Next(FallbackOpenings.Length)]);
        var tips = FallbackTips.OrderBy(_ => rng.Next()).Take(2);
        parts.Add("可以试着这样照顾自己：\n" + string.Join("\n", tips.Select(t => "· " + t)));
        parts.Add(FallbackEnding);
        return (string.Join("\n\n", parts), "local");
    }

    public async Task<(string Text, string Engine)> AssessAsync(
        string scaleType, int? score, string level, string freeText, bool crisis)
    {
        var scaleName = scaleType switch
        {
            "phq9" => "PHQ-9 抑郁筛查",
            "gad7" => "GAD-7 焦虑筛查",
            "free" => "自由倾诉",
            _ => "心理评估",
        };
        var userPart = $"量表：{scaleName}，得分 {(score?.ToString() ?? "无")}，分级：{(string.IsNullOrEmpty(level) ? "无" : level)}。\n用户自述：{(string.IsNullOrEmpty(freeText) ? "（未填写）" : freeText)}";
        const string prompt =
            "你是公益心理评估助手。根据用户量表结果与自述，输出：1) 对结果的通俗解释（不做确诊）；" +
            "2) 可能的情绪状态分析；3) 三条自助建议；4) 明确的就医/求助指引（出现自伤念头必须给出" +
            "12356、010-82951332、110/120）。语言温暖、结构清晰、400字以内。";

        var (reply, engine) = await ChatAsync(
        [
            new Dictionary<string, string> { ["role"] = "system", ["content"] = prompt },
            new Dictionary<string, string> { ["role"] = "user", ["content"] = userPart },
        ]);

        if (engine == "llm")
        {
            if (crisis && !reply.Contains("12356", StringComparison.Ordinal)) reply = CrisisPrefix + reply;
            return (reply, engine);
        }

        var parts = new List<string>();
        if (crisis) parts.Add(CrisisPrefix);
        if (scaleType is "phq9" or "gad7")
        {
            parts.Add($"你的{scaleName}得分为 {score} 分，参考分级为「{level}」。");
            parts.Add(
                "量表反映的是近两周情绪状态的倾向，不是医学诊断。分数本身不能定义你，" +
                "但它提示我们：这段时间你的情绪确实承受了较重的负担，值得认真对待。");
        }
        else
        {
            parts.Add("从你的描述里，我读到了持续的压力与情绪消耗。即使暂时说不清原因，这些感受也是真实而重要的。");
        }
        parts.Add(
            "建议你尝试：\n" +
            "· 保持规律作息与基本进食，情绪低谷期身体稳定是恢复的底座；\n" +
            "· 每天安排一次轻度活动（散步10-20分钟）与一次社交连接（给信任的人发条消息）；\n" +
            "· 用呼吸或着陆练习应对急性情绪波峰，并记录情绪与触发事件，便于复诊时和医生沟通。");

        var severe = level is "中重度" or "重度" || crisis;
        if (severe)
        {
            parts.Add(
                "求助指引：当前结果建议你尽快到正规医院心理科/精神科做一次专业评估；" +
                "若已有伤害自己的念头或计划，请立刻拨打 12356 或 010-82951332，紧急情况下拨打 110/120，不要独处。");
        }
        else if (level == "中度")
        {
            parts.Add("求助指引：建议在两周内预约学校/社区心理咨询或医院心理科，专业支持能帮你更快恢复。");
        }
        else
        {
            parts.Add("求助指引：如果两周后状态没有改善，或开始影响睡眠、饮食、学习工作，请及时寻求专业咨询。");
        }
        return (string.Join("\n\n", parts), "local");
    }

    /// <summary>
    /// 审核通过后确保该帖子有一条 AI 自动回复。已有回复时**保持现状、不重新调用大模型**——
    /// 隐藏后重新展示不应再产生一次调用；只有完全没有回复时才排队生成。
    /// 历史遗留的多余回复会在此收敛为最新的一条（不调用大模型）。
    /// </summary>
    public async Task EnsureForumReplyAsync(int postId, string content, bool crisis)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var existing = await db.MhopReplies
                .Where(r => r.PostId == postId && r.IsAi)
                .OrderByDescending(r => r.Id)
                .ToListAsync();

            if (existing.Count > 0)
            {
                // 保留最新一条（最贴近当前正文），清掉历史遗留的多余回复及其日志 / 点赞
                var extra = existing.Skip(1).ToList();
                if (extra.Count > 0)
                {
                    var extraIds = extra.Select(r => r.Id).ToList();
                    db.MhopAiLogs.RemoveRange(await db.MhopAiLogs
                        .Where(l => l.ReplyId.HasValue && extraIds.Contains(l.ReplyId.Value)).ToListAsync());
                    db.MhopLikes.RemoveRange(await db.MhopLikes
                        .Where(l => l.TargetType == "reply" && extraIds.Contains(l.TargetId)).ToListAsync());
                    db.MhopReplies.RemoveRange(extra);
                    await db.SaveChangesAsync();
                    _logger.LogInformation("帖子 {PostId} 清理历史重复 AI 回复 {Count} 条", postId, extra.Count);
                }

                _logger.LogInformation("帖子 {PostId} 已有 AI 回复，保持现状不重新生成", postId);
                return;
            }
        }

        QueueForumReply(postId, content, crisis);
    }

    /// <summary>
    /// 强制刷新某帖子的 AI 自动回复：先清掉该帖历史的 AI 回复
    /// （重复生成、正文变更等遗留），再排队生成一条新的，保证公开页面上的
    /// AI 解读与当前正文一致，且一条帖子只有一条 AI 回复。
    /// </summary>
    public async Task RegenerateForumReplyAsync(int postId, string content, bool crisis)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var stale = await db.MhopReplies.Where(r => r.PostId == postId && r.IsAi).ToListAsync();
            if (stale.Count > 0)
            {
                var ids = stale.Select(r => r.Id).ToList();
                db.MhopAiLogs.RemoveRange(await db.MhopAiLogs
                    .Where(l => l.ReplyId.HasValue && ids.Contains(l.ReplyId.Value)).ToListAsync());
                db.MhopLikes.RemoveRange(await db.MhopLikes
                    .Where(l => l.TargetType == "reply" && ids.Contains(l.TargetId)).ToListAsync());
                db.MhopReplies.RemoveRange(stale);
                await db.SaveChangesAsync();
                _logger.LogInformation("清理帖子 {PostId} 的历史 AI 回复 {Count} 条后重新生成", postId, stale.Count);
            }
        }

        QueueForumReply(postId, content, crisis);
    }

    /// <summary>
    /// 后台任务：独立 DI 作用域调用 AI 并把回复以「已通过」状态入库，
    /// 同时写入 AI 调用日志（关联 reply_id，供后台日志页撤回/恢复）。不阻塞审核请求。
    /// 写入前复核帖子仍公开且尚无 AI 回复，避免出现重复或与正文脱节的解读。
    /// </summary>
    public void QueueForumReply(int postId, string content, bool crisis)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var (text, engine) = await ForumReplyAsync(content, crisis);
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();

                // 生成期间帖子可能已被驳回或删除，或已存在回复：放弃写入
                var post = await db.MhopPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId);
                if (post is null || post.Status != 1)
                {
                    _logger.LogInformation("帖子 {PostId} 已不在公开状态，跳过 AI 自动回复", postId);
                    return;
                }
                if (await db.MhopReplies.AnyAsync(r => r.PostId == postId && r.IsAi))
                {
                    _logger.LogInformation("帖子 {PostId} 已有 AI 自动回复，跳过重复生成", postId);
                    return;
                }

                var reply = new MhopReply
                {
                    PostId = postId,
                    UserId = null,
                    IsAnonymous = true,
                    Content = text,
                    Status = 1,
                    IsAi = true,
                    Crisis = crisis,
                    CreatedAt = DateTime.UtcNow,
                };
                db.MhopReplies.Add(reply);
                await db.SaveChangesAsync();
                db.MhopAiLogs.Add(new MhopAiLog
                {
                    UserId = null,
                    Module = "forum",
                    ReplyId = reply.Id,
                    Prompt = Truncate(content, 2000),
                    Response = Truncate(text, 4000),
                    Engine = engine,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成 AI 自动回复失败：postId={PostId}", postId);
            }
        });
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
