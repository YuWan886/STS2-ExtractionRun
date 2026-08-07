using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
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

        using (RitsuLibFramework.BeginModDataRegistration(ModId))
        {
            WarehouseStore.Register();
            PendingCarryStore.Register();
            ExtractionRunData.Register();
            ExtractionSettingsPage.Register();
        }

        ExtractionRunEnd.Register();

        Harmony harmony = new(ModId);
        harmony.PatchAll(assembly);

        Logger.Info("ExtractionRun initialized.");
    }
}
