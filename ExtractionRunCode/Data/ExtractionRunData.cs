using STS2RitsuLib;
using STS2RitsuLib.RunData;

namespace ExtractionRun.Data;

/// <summary>
/// Central holder for the per-run <c>PlayerRunSavedData</c> slot carrying each player's <see cref="CarryConfig"/>.
/// Registered in <c>Entry</c>; MP-synced via RitsuLib lobby staging (<c>SyncLobbyOnChange</c>). Imported into the run
/// state before <c>InitializeNewRun</c> runs, so the modifier can read it inside <c>AfterRunCreated</c> on every machine.
/// 每名玩家的局内携带配置槽位；联机时经 RitsuLib 大厅暂存同步，并在 InitializeNewRun 前导入，修正项可在 AfterRunCreated 读取。
/// </summary>
public static class ExtractionRunData
{
    public const string CarryKey = "carry";

    public static PlayerRunSavedData<CarryConfig> Carry { get; private set; } = null!;

    /// <summary>Registers the run saved-data slot. Must run inside <c>BeginModDataRegistration</c>.</summary>
    public static void Register()
    {
        var store = RitsuLibFramework.GetRunSavedDataStore(Entry.ModId);
        Carry = store.RegisterPerPlayer<CarryConfig>(
            key: CarryKey,
            defaultFactory: () => new CarryConfig(),
            options: new RunSavedDataOptions
            {
                WritePolicy = RunSavedDataWritePolicy.WhenSet,
                SyncLobbyOnChange = true,
            });
    }
}
