namespace ExtractionRun.Data;

/// <summary>
/// Persistent per-profile challenge state (ModDataStore, SaveScope.Profile). The daily pool is rolled once per local
/// calendar day (first hub open that day, mirroring <see cref="ShopStore"/>) — a refresh via the console command re-rolls
/// it on demand. Daily challenges are re-selectable and grant their reward on every clear (grill-locked: no completion
/// gate — the player chose to allow unlimited farming). Permanent clears accumulate (page ✓).
/// 每个存档位一份的挑战状态。每日池在本地日历日首次打开时 roll 一次（镜像 ShopStore）；控制台 refresh 指令可即时重 roll。
/// 每日挑战可重复选择、每次通关都发奖励（grill 锁定：无完成闸门——玩家选择允许无限刷）。常驻通关标记累积（页面打勾）。
/// </summary>
public sealed class ChallengeData
{
    /// <summary>Local date (yyyy-MM-dd) the current daily pool was rolled for. 当前每日池所属的本地日期。</summary>
    public string DailyDate { get; set; } = "";

    /// <summary>The day's rolled daily challenge ids (deduped). 本日 roll 出的每日挑战 id（去重）。</summary>
    public List<string> DailyIds { get; set; } = new();

    /// <summary>Daily challenge clear counts (id → total clears). 每日挑战累计通关次数（id → 通关数）。</summary>
    public Dictionary<string, int> DailyClearCounts { get; set; } = new();

    /// <summary>Permanent challenge ids cleared at least once (✓ on the page, accumulates). 通关过的常驻挑战 id（打勾，累积）。</summary>
    public List<string> PermanentCleared { get; set; } = new();

    /// <summary>Permanent challenge clear counts (id → total clears). 常驻挑战累计通关次数（id → 通关数）。</summary>
    public Dictionary<string, int> PermanentClearCounts { get; set; } = new();
}
