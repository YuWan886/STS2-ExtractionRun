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
    // ModId 需要和 ExtractionRun.json 里的 id 保持一致。res://ExtractionRun/ 是 PCK 资源目录。
    public const string ModId = "ExtractionRun";

    public static Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        // Godot C# 脚本注册（让 pck 中的脚本类型能被 Godot 找到）与 RitsuLib 内容自动注册都要保留。
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
