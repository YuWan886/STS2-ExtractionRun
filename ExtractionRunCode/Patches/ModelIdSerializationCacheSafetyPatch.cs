using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace ExtractionRun.Patches;

/// <summary>
///     Guarantees the content-variant assembly's <see cref="AbstractModel"/> subtypes reach the game's net-ID map.
///     The game builds <c>ModelIdSerializationCache</c> by scanning each mod's assembly (<c>mod.assembly</c> = the
///     ExtractionRun loader) via <c>ReflectionHelper.GetSubtypesFromAssembly</c>; the loader bridges the content-variant
///     types into that scan. When that bridge doesn't fire, <c>ExtractionModifier</c> gets no net ID and
///     <c>ModelDb.InitIds</c> throws ("could not be mapped to any net ID") at startup. RitsuLib's own deterministic
///     rebuild only runs when the PC has RitsuLib-registered content, so a PC running only this mod crashes.
///     This postfix re-appends this assembly's model types (matched by the loader assembly name, not instance),
///     deduplicated so a working loader bridge isn't double-counted, letting the game compute the canonical maps and
///     hash itself.
///     兜底：确保内容变体程序集的 AbstractModel 子类进入游戏的 net-ID 映射。游戏通过 GetSubtypesFromAssembly 扫描
///     mod.assembly（加载器）构建缓存；当加载器桥接未生效时 ExtractionModifier 拿不到 net ID，ModelDb.InitIds 启动即崩
///     （RitsuLib 的确定性重建仅在存在 RitsuLib 注册内容时运行）。本 postfix 按加载器程序集名（而非实例）重新追加本
///     程序集的模型类型，去重避免与加载器桥接叠加，由游戏自身计算规范的映射与 hash。
/// </summary>
[HarmonyPatch(typeof(ReflectionHelper), nameof(ReflectionHelper.GetSubtypesFromAssembly))]
internal static class ModelIdSerializationCacheSafetyPatch
{
    private static readonly Type[]? ContentModels = TryGetContentModels();

    private static void Postfix(Assembly assembly, Type parentType, ref IEnumerable<Type> __result)
    {
        Type[]? contentModels = ContentModels;
        if (contentModels is null || contentModels.Length == 0)
        {
            return;
        }

        if (assembly.GetName().Name != "ExtractionRun")
        {
            return;
        }

        List<Type> existing = __result.ToList();
        Type[] extra = contentModels
            .Where(type => !type.IsAbstract && !type.IsInterface &&
                           ReflectionHelper.InheritsOrImplements(type, parentType) &&
                           !existing.Contains(type))
            .ToArray();
        if (extra.Length == 0)
        {
            return;
        }

        __result = existing.Concat(extra);
    }

    private static Type[]? TryGetContentModels()
    {
        try
        {
            return typeof(ModelIdSerializationCacheSafetyPatch).Assembly.GetTypes()
                .Where(type => typeof(AbstractModel).IsAssignableFrom(type))
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>()
                .Where(type => typeof(AbstractModel).IsAssignableFrom(type))
                .ToArray();
        }
        catch
        {
            return null;
        }
    }
}
