using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Compatibility;

/// <summary>
/// Registers the content assembly's <c>[SavedProperty]</c> models with the game's saved-property cache on hosts that
/// use the legacy <c>SavedPropertiesTypeCache</c> (0.107.1). On 0.111+ the unified
/// <c>ModelIdSerializationCache</c> scans every <c>ModelDb</c> type natively during <c>Init</c>, so this no-ops there
/// (the type simply isn't present in the game assembly). Without it, <c>ExtractionModifier</c>'s persisted
/// placement/gate props never round-trip through <c>SerializableModifier.Props</c> on legacy hosts and the 撤离点
/// vanishes after a mid-run save/reload.
/// Runs in <c>Entry.Initialize</c> (mod init) — before RitsuLib's own cache injection at <c>LocManager.Initialize</c>,
/// whose net-ID sort (when enabled) and <c>RefreshNetIdBitSize</c> run afterwards, so the table stays deterministic.
/// 在旧版 SavedPropertiesTypeCache（0.107.1）上注册内容程序集的 [SavedProperty] 模型。0.111+ 的统一
/// ModelIdSerializationCache 会在 Init 时原生扫描每个 ModelDb 类型，此方法自动跳过（该类型在游戏程序集中不存在）。否则旧版
/// 主机上 ExtractionModifier 的持久化属性不会经 SerializableModifier.Props 往返，中途读档后撤离点消失。在 Entry.Initialize
/// （模组加载期）调用——先于 RitsuLib 在 LocManager.Initialize 的注入，其后的 net-ID 排序（开启时）与 RefreshNetIdBitSize
/// 保证顺序与位宽确定。
/// </summary>
public static class SavedPropertyCacheInjection
{
    public static void Register()
    {
        try
        {
            Type? cacheType = typeof(ModifierModel).Assembly.GetType(
                "MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache");
            MethodInfo? inject = cacheType?.GetMethod(
                "InjectTypeIntoCache", BindingFlags.Public | BindingFlags.Static);
            if (inject is null)
            {
                return; // 0.111+ unified cache registers [SavedProperty] natively.
            }

            foreach (Type model in GetContentModels())
            {
                if (model.IsAbstract || model.IsInterface ||
                    model.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .All(property => property.GetCustomAttribute<SavedPropertyAttribute>() is null))
                {
                    continue;
                }

                inject.Invoke(null, new object[] { model });
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"SavedPropertyCacheInjection failed: {ex.Message}");
        }
    }

    private static IEnumerable<Type> GetContentModels()
    {
        try
        {
            return typeof(SavedPropertyCacheInjection).Assembly.GetTypes()
                .Where(type => typeof(AbstractModel).IsAssignableFrom(type));
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>()
                .Where(type => typeof(AbstractModel).IsAssignableFrom(type));
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
