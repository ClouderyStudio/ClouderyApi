namespace ClouderyApi.Modules.Cloudery.Api.Contracts;

/// <summary>
/// 结果 AI 解读请求体。站点只提交解读必需的字段：不提交姓名、账号等身份信息，
/// 私人备注也只在用户勾选同意后由前端放入 <see cref="Note"/>。
/// </summary>
public sealed class ResultAnalysisIn
{
    /// <summary>量表标识，如 scl90 / mbti；服务端据此选择解读侧重。</summary>
    public string TestId { get; set; } = string.Empty;

    /// <summary>量表中文名，用于提示词与兜底文案。</summary>
    public string? TestTitle { get; set; }

    /// <summary>量表类别（来自站点 tests/list 的 category，如 symptom / personality）。</summary>
    public string? Category { get; set; }

    /// <summary>
    /// 结果类型：severity = 计分型（分数即困扰程度）、trait = 特质型（分数只表示相对倾向）、
    /// type = 类型型（MBTI / 气质类型 / 心理年龄等，结果是类型或画像，分数没有高低含义）。
    /// 站点未提供时由服务端按 testId 与 category 推断。
    /// </summary>
    public string? ScoreKind { get; set; }

    public double? TotalScore { get; set; }

    public double? MaxScore { get; set; }

    public double? MinScore { get; set; }

    /// <summary>结果等级文案，如「中度」「筛查阳性」。</summary>
    public string? Level { get; set; }

    /// <summary>0-1 的严重程度（站点 severity）。</summary>
    public double? Severity { get; set; }

    /// <summary>分数口径说明（仅部分量表下发）。</summary>
    public string? ScoreNote { get; set; }

    /// <summary>站点自带的分档建议文案（作为解读补充）。</summary>
    public string? Suggestion { get; set; }

    /// <summary>作答时间范围，如「近一周」。</summary>
    public string? TimeFrame { get; set; }

    /// <summary>作答人（本人 / 家长代评等）。</summary>
    public string? Respondent { get; set; }

    /// <summary>站点判定的风险标记（SIOSS 筛查阳性等），为 true 时强制给出援助热线。</summary>
    public bool Risk { get; set; }

    /// <summary>用户的私人备注；仅在用户明确同意后才会带上。</summary>
    public string? Note { get; set; }

    /// <summary>维度分数（站点会把各种形态的 dimensionScores 归一成该结构）。</summary>
    public List<ResultAnalysisDimension>? Dimensions { get; set; }

    /// <summary>
    /// 量表画像数据。类型型量表（MBTI、七美德与七宗罪、心理年龄）与多维自评量表的
    /// 结果主体不是分数，而是类型名、指数、双轴、效度等结构化内容，由站点整理成短句送来，
    /// 让模型有具体材料可剖析，而不是只能复述总分。
    /// </summary>
    public List<ResultAnalysisProfileItem>? Profile { get; set; }
}

/// <summary>量表画像中的一条：名称 + 取值文案。</summary>
public sealed class ResultAnalysisProfileItem
{
    public string? Name { get; set; }

    public string? Value { get; set; }
}

/// <summary>归一后的维度分数。</summary>
public sealed class ResultAnalysisDimension
{
    public string? Name { get; set; }

    public double? Score { get; set; }

    /// <summary>该维度的满分（可选）。</summary>
    public double? Max { get; set; }

    /// <summary>该维度的分级文案（可选），如「轻度」。</summary>
    public string? Level { get; set; }

    /// <summary>该维度自带的解释文案（可选），如「自我与经验基本和谐，偶尔感到矛盾」。</summary>
    public string? Desc { get; set; }
}

/// <summary>结果 AI 解读响应体（裸对象，与 Cloudery 模块其它接口一致）。</summary>
public sealed class ResultAnalysisOut
{
    /// <summary>四段式解读正文，始终非空（模型不可用时为本地兜底文案）。</summary>
    public string Analysis { get; set; } = string.Empty;

    /// <summary>引擎标识：llm = 大模型生成，local = 本地兜底。</summary>
    public string Engine { get; set; } = "local";

    /// <summary>是否判定为危机场景（正文开头已强制带援助信息）。</summary>
    public bool Crisis { get; set; }

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
