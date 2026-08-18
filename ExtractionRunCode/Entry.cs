using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using ExtractionRun.Compatibility;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Settings;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;

namespace ExtractionRun;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public const string ModId = "ExtractionRun";

    public static Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);

        // Legacy hosts (0.107.1) need the content models' [SavedProperty] props registered in their
        // SavedPropertiesTypeCache; 0.111+ registers them natively. 旧版主机需手动注册内容模型的 [SavedProperty] 属性。
        SavedPropertyCacheInjection.Register();

        using (RitsuLibFramework.BeginModDataRegistration(ModId))
        {
            WarehouseStore.Register();
            PendingCarryStore.Register();
            ShopStore.Register();
            ChallengeStore.Register();
            ExtractionRunData.Register();
            ExtractionSettingsPage.Register();
        }

        ExtractionRunEnd.Register();

        Harmony harmony = new(ModId);
        harmony.PatchAll(assembly);

        // Some hosts (beta) reject modifier/seed changes on the character-select lobby; adapt at runtime.
        ModifierChangeCompat.InstallIfNeeded(harmony);
        SeedChangeCompat.InstallIfNeeded(harmony);

        // The STS2-Game-Lobby mod can host extraction rooms; bridge its create flow into the launch chain when present.
        // 联机大厅 mod 可托管搜打撤房间；检测到它时把其建房流程桥接进开跑链路。
        LanConnectCompat.InstallIfNeeded(harmony);

        Logger.Info("ExtractionRun initialized.");
    }
}
