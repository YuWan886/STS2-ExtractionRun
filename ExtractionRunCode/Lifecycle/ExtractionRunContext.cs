using MegaCrit.Sts2.Core.Models;

namespace ExtractionRun.Lifecycle;

/// <summary>
/// Transient process-wide state for the extraction-mode launch flow.
/// <list type="bullet">
/// <item><c>IsExtractionLaunch</c>: set by the warehouse hub before launching a run; consumed by the character-select
/// lobby patch to apply the extraction modifier on the lobby.</item>
/// <item><c>PendingRunModifiers</c>: the modifiers captured by the <c>NCharacterSelectScreen.BeginRun</c> prefix and
/// forwarded to <c>NGame.StartNewSingleplayerRun/StartNewMultiplayerRun</c>, which is where the base game would
/// otherwise drop them (it passes an empty array).</item>
/// </list>
/// 搜打撤发起跑局时的临时进程状态：是否由仓库大厅发起、以及从 BeginRun 暂存、转发到 NGame.Start*Run 的修正项。
/// </summary>
public static class ExtractionRunContext
{
    /// <summary>True while the warehouse hub is launching a 搜打撤 run. 由仓库大厅发起跑局时为 true。</summary>
    public static bool IsExtractionLaunch { get; set; }

    /// <summary>Modifiers captured from <c>NCharacterSelectScreen.BeginRun</c>, awaiting forwarding to NGame. 待前向转发的修正项。</summary>
    public static IReadOnlyList<ModifierModel>? PendingRunModifiers { get; set; }

    /// <summary>Clears the launch flow state (after a run starts or the flow is cancelled).</summary>
    public static void Clear()
    {
        IsExtractionLaunch = false;
        PendingRunModifiers = null;
    }
}
