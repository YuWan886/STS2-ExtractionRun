namespace ExtractionRun.Settings;

/// <summary>
/// Global 搜打撤 settings (not per-profile). Persisted via RitsuLib ModDataStore at <c>SaveScope.Global</c>.
/// 搜打撤全局设置（不分存档位），经 ModDataStore 以 SaveScope.Global 持久化。
/// </summary>
public sealed class ExtractionSettings
{
    /// <summary>Maximum number of cards a player may carry into a run. 每局最多携带的卡牌数。</summary>
    public int MaxCarryCards { get; set; } = 10;

    /// <summary>Maximum number of relics a player may carry into a run. 每局最多携带的遗物数。</summary>
    public int MaxCarryRelics { get; set; } = 3;

    /// <summary>Whether hovering a card tile shows the vanilla tooltip. 悬停卡牌瓦片是否显示原版提示框。</summary>
    public bool ShowCardHoverTips { get; set; } = true;

    /// <summary>Whether hovering a relic tile shows the vanilla tooltip. 悬停遗物瓦片是否显示原版提示框。</summary>
    public bool ShowRelicHoverTips { get; set; } = true;

    /// <summary>Whether hovering a potion tile shows the vanilla tooltip. 悬停药水瓦片是否显示原版提示框。</summary>
    public bool ShowPotionHoverTips { get; set; } = true;

    public void ResetToDefaults()
    {
        MaxCarryCards = 10;
        MaxCarryRelics = 3;
        ShowCardHoverTips = true;
        ShowRelicHoverTips = true;
        ShowPotionHoverTips = true;
    }
}
