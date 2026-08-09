using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib.Content;

namespace ExtractionRun.Data;

/// <summary>
/// Resolves which mod owns a carried item (for the gear code's bracket annotation) and maps normalized mod-id stems to
/// loaded-mod display names (for the import dialog's missing-mod report). Resolution order: RitsuLib's content registry
/// (<see cref="ModContentRegistry.TryGetOwnerModId"/>) → matching the model's assembly to a loaded mod (attribution for
/// any framework, e.g. YuWanCard/BaseLib/plain-Harmony content, regardless of public-entry convention) → matching the
/// entry's first underscore-segment to a loaded mod's normalized id (best-effort when the model itself can't be resolved,
/// or for content whose assembly isn't associated). Items that match nothing are treated as base content (no annotation).
/// The loaded-mod table is built once (mods don't change mid-session) and cached for the process.
/// 解析物品的归属 mod（用于战备码标注）与规范化 mod id → 显示名（用于导入的缺 mod 报告）。解析顺序：RitsuLib 内容注册表 →
/// 按模型程序集匹配已加载 mod（覆盖任何框架内容，如 YuWanCard/BaseLib/纯 Harmony，不依赖公开 entry 约定）→ entry 首段匹配
/// 已加载 mod 的规范化 id（模型本身解析不到时的兜底）。匹配不到的按基础内容处理。已加载 mod 表只构建一次并缓存。
/// </summary>
public static class CarryCodeOwner
{
    private static readonly BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static List<LoadedModInfo>? _loadedMods;

    /// <summary>
    /// Resolves the normalized mod-id stem (bracket annotation) for an item id, or null for base content.
    /// 解析物品归属的规范化 mod id，基础内容返回 null。
    /// </summary>
    public static string? ResolveOwnerStem(CarryCodec.ItemKind kind, ModelId id)
    {
        AbstractModel? model = kind switch
        {
            CarryCodec.ItemKind.Card => ModelDb.GetByIdOrNull<CardModel>(id),
            CarryCodec.ItemKind.Relic => ModelDb.GetByIdOrNull<RelicModel>(id),
            _ => ModelDb.GetByIdOrNull<PotionModel>(id),
        };

        if (model != null)
        {
            if (ModContentRegistry.TryGetOwnerModId(model.GetType(), out string modId))
            {
                return NormalizeStem(modId);
            }

            Assembly modelAssembly = model.GetType().Assembly;
            foreach (LoadedModInfo info in LoadedMods)
            {
                if (info.Assemblies.Contains(modelAssembly))
                {
                    return info.Stem;
                }
            }
        }

        string entry = id.Entry;
        int underscore = entry.IndexOf('_');
        string firstSegment = underscore < 0 ? entry : entry.Substring(0, underscore);
        return firstSegment.Length > 0 && LoadedMods.Any(m => m.Stem == firstSegment) ? firstSegment : null;
    }

    /// <summary>True when a mod whose normalized id equals <paramref name="stem"/> is loaded. 是否存在规范化 id 等于 stem 的已加载 mod。</summary>
    public static bool IsModLoaded(string stem) => LoadedMods.Any(m => m.Stem == stem);

    /// <summary>Display name for a mod stem: the loaded mod's manifest name, or the stem itself when not loaded.
    /// mod stem 的显示名：已加载则用清单名，否则返回 stem 本身。</summary>
    public static string ResolveModDisplayName(string stem)
    {
        foreach (LoadedModInfo info in LoadedMods)
        {
            if (info.Stem == stem)
            {
                return info.DisplayName;
            }
        }

        return stem;
    }

    private static string? NormalizeStem(string modId)
    {
        try
        {
            string stem = ModContentRegistry.NormalizePublicStem(modId);
            return stem.Length > 0 ? stem : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<LoadedModInfo> LoadedMods
    {
        get
        {
            if (_loadedMods == null)
            {
                var list = new List<LoadedModInfo>();
                try
                {
                    foreach (Mod mod in ModManager.Mods)
                    {
                        try
                        {
                            string? id = mod.manifest?.id;
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            if (NormalizeStem(id) is not string stem)
                            {
                                continue;
                            }

                            string? name = mod.manifest?.name;
                            list.Add(new LoadedModInfo
                            {
                                Stem = stem,
                                DisplayName = string.IsNullOrWhiteSpace(name) ? id : name,
                                Assemblies = ModAssemblies(mod),
                            });
                        }
                        catch (Exception)
                        {
                            // A single unreadable mod must not abort the whole enumeration.
                        }
                    }
                }
                catch (Exception ex)
                {
                    Entry.Logger.Warn($"CarryCodeOwner: failed to enumerate loaded mods: {ex.Message}");
                }

                _loadedMods = list;
            }

            return _loadedMods;
        }
    }

    /// <summary>
    /// Reads a mod's assembly list across game versions (0.108+ exposes <c>assemblies</c>; 0.107.1 has a single
    /// <c>assembly</c>). Defensive: any drift just yields no match and falls through to the entry-prefix heuristic.
    /// 跨版本读取 mod 的程序集列表（0.108+ 为 assemblies，0.107.1 为单个 assembly）。防御式：读取失败即无匹配，回退到 entry 首段。
    /// </summary>
    private static IReadOnlyList<Assembly> ModAssemblies(Mod mod)
    {
        try
        {
            if (typeof(Mod).GetField("assemblies", FieldFlags)?.GetValue(mod) is IEnumerable<Assembly> list)
            {
                return list.Where(a => a != null).ToArray();
            }
        }
        catch (Exception)
        {
            // Fall through to the single-assembly field below.
        }

        try
        {
            if (typeof(Mod).GetField("assembly", FieldFlags)?.GetValue(mod) is Assembly single)
            {
                return single == null ? Array.Empty<Assembly>() : new[] { single };
            }
        }
        catch (Exception)
        {
            // No assembly info readable — caller falls back to the entry-prefix heuristic.
        }

        return Array.Empty<Assembly>();
    }

    private sealed class LoadedModInfo
    {
        public required string Stem { get; init; }

        public required string DisplayName { get; init; }

        public IReadOnlyList<Assembly> Assemblies { get; init; } = Array.Empty<Assembly>();
    }
}
