using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace ExtractionRun.Compatibility;

/// <summary>
///     Forces the game's <c>ModelIdSerializationCache.Init</c> to run through the loader's reflection bridge.
///
///     The game ships IL-only. <c>Init</c> can be JIT-compiled early (e.g. during mod loading, before the loader's
///     <c>GetSubtypesFromAssembly</c> patch is installed) with the pre-patch original <b>inlined</b>, so the mod-scan
///     silently skips the content-variant model types. The result: <c>EXTRACTION_MODIFIER</c> gets no net ID and
///     <c>ModelDb.InitIds</c> throws ("could not be mapped to any net ID") at startup. Installing ANY Harmony patch on
///     <c>Init</c> forces a fresh JIT compile through the Harmony wrapper, which calls the bridged
///     <c>GetSubtypesFromAssembly</c> and lands the content types.
///
///     This postfix also verifies the entry landed and re-runs <c>Init</c> once if it did not (the maps dedup by key,
///     so the re-run is idempotent) — defense-in-depth for hosts where the bridge still does not fire.
///     兜底：强制游戏 ModelIdSerializationCache.Init 走加载器反射桥。游戏为纯 IL；Init 可能在模组加载期被提前 JIT
///     编译（内联了未打补丁的 GetSubtypesFromAssembly），导致 mod 扫描漏掉内容变体的模型类型，EXTRACTION_MODIFIER
///     拿不到 net ID，ModelDb.InitIds 启动即崩。对 Init 挂任何 Harmony 补丁都会强制其经 Harmony 包装器重新编译，
///     从而调用带桥的 GetSubtypesFromAssembly。本 postfix 还校验条目是否落地，未落地则再跑一次 Init（映射按键去重，
///     幂等）作为兜底。
/// </summary>
[HarmonyPatch(typeof(ModelIdSerializationCache), nameof(ModelIdSerializationCache.Init))]
internal static class ModelIdSerializationCacheBridgePatch
{
    private static int _repairAttempted;

    private static void Postfix()
    {
        try
        {
            if (ModelIdSerializationCache.TryGetNetIdForEntry("EXTRACTION_MODIFIER", out _))
                return;

            if (Interlocked.Exchange(ref _repairAttempted, 1) != 0)
                return;

            Entry.Logger.Warn(
                "EXTRACTION_MODIFIER missing from the net-ID cache after ModelIdSerializationCache.Init; " +
                "re-running Init to pick up bridged content types.");
            ModelIdSerializationCache.Init();
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"ModelIdSerializationCacheBridgePatch failed: {ex}");
        }
    }
}
