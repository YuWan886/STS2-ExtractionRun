using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using ExtractionRun.Data;
using ExtractionRun.UI;

namespace ExtractionRun.Settings;

/// <summary>
/// Core of the 存档管理 (save transfer) feature: exports the complete Search-Loot-Extract save (the five
/// profile-scoped slots + the global settings slot) into a ZIP (manifest.json + one JSON per slot), reads and strictly
/// validates such a ZIP back, and applies an import through the live ModDataStore instances — immediate effect without
/// a restart, because a direct disk overwrite would be clobbered by the in-memory cache on the next Save. Also creates
/// automatic backups of the current state (a regular export, so a backup can be restored through the same import flow).
/// 存档管理核心：把搜打撤完整存档（5 个按存档位槽 + 1 个全局设置槽）导出为 ZIP（manifest.json + 每槽一个 JSON），
/// 读回并严格校验此类 ZIP，并通过 ModDataStore 活实例应用导入——立即生效、无需重启（直接覆写磁盘会被内存缓存在下一次
/// Save 时覆盖回来）。同时支持自动备份当前状态（普通导出，因此备份也可通过同一导入流程恢复）。
/// </summary>
public static class SaveTransfer
{
    /// <summary>Current export format version. Bump when the ZIP layout or the slot JSON shapes change; imports with a
    /// different version are rejected. 当前导出格式版本；ZIP 结构或槽 JSON 形状变化时递增，版本不符的导入会被拒绝。</summary>
    public const int FormatVersion = 1;

    public const string ManifestName = "manifest.json";

    /// <summary>Serialization options matching RitsuLib's ModDataStore persistence (plain <c>new JsonSerializerOptions()</c>;
    /// legacy shapes and the durability wrappers are carried by type-level [JsonConverter] attributes).
    /// 与 RitsuLib ModDataStore 持久化一致的序列化选项（默认选项；旧版形状与耐久包装由类型级 JsonConverter 处理）。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>The five profile-scoped slots (file name in the ZIP == the mod's slot file name). 五个按存档位的数据槽。</summary>
    private static readonly (string FileName, string Key)[] ProfileSlots =
    {
        ("warehouse.json", WarehouseStore.DataKey),
        ("warehouse_nodur.json", WarehouseStore.NoDurabilityDataKey),
        ("pending_carry.json", PendingCarryStore.DataKey),
        ("shop.json", ShopStore.DataKey),
        ("challenge.json", ChallengeStore.DataKey),
    };

    /// <summary>The global settings slot (not profile-scoped). 全局设置槽（不分存档位）。</summary>
    private static readonly (string FileName, string Key) SettingsSlot = ("settings.json", ExtractionSettingsPage.DataKey);

    private static IEnumerable<(string FileName, string Key)> AllSlots => ProfileSlots.Append(SettingsSlot);

    /// <summary>An export manifest; validation only checks <see cref="FormatVersion"/>. 导出清单；校验只认 FormatVersion。</summary>
    public sealed class ExportManifest
    {
        public int FormatVersion { get; set; }
        public string ModVersion { get; set; } = "";
        public string ExportedAt { get; set; } = "";
        public int ProfileId { get; set; }
    }

    /// <summary>One validated slot read from an import archive. 从导入包中读取并通过校验的一个槽。</summary>
    public sealed class ImportedSlot
    {
        public required string FileName { get; init; }
        public required string Key { get; init; }
        public required object Data { get; init; }
    }

    /// <summary>How an import treats slots missing from the archive. 导入对「压缩包中缺失的槽」的处理方式。</summary>
    public enum ImportMode
    {
        /// <summary>Full restore: slots present in the archive overwrite local; missing slots reset to defaults.
        /// 完整恢复：包中有的槽覆盖本地，缺失的槽重置为默认。</summary>
        Overwrite,

        /// <summary>Per-slot merge: slots present in the archive overwrite local; missing slots keep local data.
        /// 按槽合并：包中有的槽覆盖本地，缺失的槽保留本地。</summary>
        Merge,
    }

    /// <summary>Thrown with a pre-localized message when an import file fails validation. 导入文件校验失败时抛出（消息已本地化）。</summary>
    public sealed class SaveTransferException : Exception
    {
        public SaveTransferException(string message) : base(message) { }
    }

    /// <summary>
    /// True when RitsuLib's mod-data cloud mirror toggle is enabled. The store class is internal, so it is read
    /// reflectively — the toggle governs whether every <c>Save(key)</c> auto-pushes that slot's file to Steam cloud.
    /// 是否开启 RitsuLib 的 mod 数据云同步开关（内部类，反射读取）。开关开启时每次 Save(key) 都会自动把该槽文件推上云。
    /// </summary>
    public static bool IsCloudSyncEnabled()
    {
        try
        {
            Type? type = Type.GetType("STS2RitsuLib.Data.RitsuLibSettingsStore, STS2RitsuLib");
            MethodInfo? method = type?.GetMethod(
                "IsSyncModDataToCloudEnabled", BindingFlags.Static | BindingFlags.NonPublic);
            return method != null && (bool)(method.Invoke(null, null) ?? false);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"SaveTransfer.IsCloudSyncEnabled: {ex.Message}");
            return false;
        }
    }

    /// <summary>Current game save-slot id (0 when unavailable). 当前游戏存档位 id（不可用时为 0）。</summary>
    public static int CurrentProfileId
    {
        get
        {
            try
            {
                return SaveManager.Instance.CurrentProfileId;
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"SaveTransfer.CurrentProfileId: {ex.Message}");
                return 0;
            }
        }
    }

    /// <summary>The mod version recorded in export manifests (from the game's mod manager, falling back to the assembly
    /// version). 导出清单中记录的 mod 版本（取自游戏的 mod 管理器，回退到程序集版本）。</summary>
    public static string GetModVersion()
    {
        try
        {
            foreach (Mod mod in ModManager.Mods)
            {
                if (mod.manifest?.id == Entry.ModId && mod.version != null)
                {
                    return mod.version.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"SaveTransfer.GetModVersion: {ex.Message}");
        }

        return typeof(SaveTransfer).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Exports the current live state of every slot into a ZIP at <paramref name="path"/> (manifest.json + one JSON
    /// entry per slot, file names matching the mod's slot files). Reading the live instances — not the disk files —
    /// guarantees the archive matches what RitsuLib would load on the next boot.
    /// 把全部槽的当前活状态导出为 <paramref name="path"/> 处的 ZIP（manifest.json + 每槽一个 JSON，文件名与 mod 槽文件一致）。
    /// 读活实例而非磁盘文件，保证包内容与下次启动 RitsuLib 载入的完全一致。
    /// </summary>
    public static void ExportTo(string path)
    {
        ModDataStore store = RitsuLibFramework.GetDataStore(Entry.ModId);

        using var fileStream = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        WriteEntry(archive, ManifestName, JsonSerializer.Serialize(new ExportManifest
        {
            FormatVersion = FormatVersion,
            ModVersion = GetModVersion(),
            ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ProfileId = CurrentProfileId,
        }, JsonOptions));

        WriteEntry(archive, "warehouse.json",
            JsonSerializer.Serialize(store.Get<WarehouseData>(WarehouseStore.DataKey), JsonOptions));
        WriteEntry(archive, "warehouse_nodur.json",
            JsonSerializer.Serialize(store.Get<WarehouseData>(WarehouseStore.NoDurabilityDataKey), JsonOptions));
        WriteEntry(archive, "pending_carry.json",
            JsonSerializer.Serialize(store.Get<CarryConfig>(PendingCarryStore.DataKey), JsonOptions));
        WriteEntry(archive, "shop.json",
            JsonSerializer.Serialize(store.Get<ShopData>(ShopStore.DataKey), JsonOptions));
        WriteEntry(archive, "challenge.json",
            JsonSerializer.Serialize(store.Get<ChallengeData>(ChallengeStore.DataKey), JsonOptions));
        WriteEntry(archive, "settings.json",
            JsonSerializer.Serialize(store.Get<ExtractionSettings>(ExtractionSettingsPage.DataKey), JsonOptions));
    }

    /// <summary>
    /// Reads and strictly validates an import ZIP: it must be a valid archive, contain a manifest with the current
    /// format version, and every recognized slot file must be valid JSON carrying the required key fields. Returns the
    /// validated slots (typed, deserialized) for the confirm step — nothing is written yet.
    /// 读取并严格校验导入包：必须是有效 ZIP、含当前格式版本的 manifest、每个可识别槽文件必须是合法 JSON 且含必要字段。
    /// 返回通过校验的槽（已类型化反序列化），供确认步骤使用——此时不写任何数据。
    /// </summary>
    public static List<ImportedSlot> ReadAndValidate(string path)
    {
        using var fileStream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read);
        using ZipArchive archive = OpenArchive(fileStream);

        ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestName);
        if (manifestEntry == null)
        {
            throw new SaveTransferException(ExtractionLocalization.SaveErrorNoManifestText());
        }

        ExportManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ExportManifest>(ReadEntry(manifestEntry), JsonOptions);
        }
        catch (JsonException)
        {
            throw new SaveTransferException(ExtractionLocalization.SaveErrorBadManifestText());
        }

        if (manifest == null || manifest.FormatVersion != FormatVersion)
        {
            throw new SaveTransferException(
                ExtractionLocalization.SaveErrorVersionText(manifest?.FormatVersion ?? 0, FormatVersion));
        }

        var slots = new List<ImportedSlot>();
        foreach ((string fileName, string key) in AllSlots)
        {
            ZipArchiveEntry? entry = archive.GetEntry(fileName);
            if (entry == null)
            {
                continue;
            }

            string json = ReadEntry(entry);
            ValidateSlotJson(fileName, json);
            slots.Add(new ImportedSlot { FileName = fileName, Key = key, Data = DeserializeSlot(fileName, json) });
        }

        if (slots.Count == 0)
        {
            throw new SaveTransferException(ExtractionLocalization.SaveErrorEmptyText());
        }

        return slots;
    }

    /// <summary>
    /// Applies validated slots through the live ModDataStore instances (immediate effect, no restart needed). In
    /// <see cref="ImportMode.Overwrite"/> slots missing from the archive are reset to defaults. Warehouse version
    /// counters are bumped so display caches invalidate. When the pending carry was part of the import it is re-validated
    /// against the (possibly imported) active warehouse — carried copies must exist there, otherwise run-start injection
    /// would grant items the warehouse no longer holds (a free-item dupe).
    /// 通过 ModDataStore 活实例应用校验通过的槽（立即生效、无需重启）。覆盖模式下，包中缺失的槽重置为默认。仓库版本号 +1
    /// 以失效展示缓存。若导入包含待发携带，则按（可能被导入的）活动仓库重新校验——携带副本必须存在于仓库，否则开跑注入会
    /// 白嫖仓库已不持有的物品。
    /// </summary>
    public static void Apply(List<ImportedSlot> slots, ImportMode mode)
    {
        ModDataStore store = RitsuLibFramework.GetDataStore(Entry.ModId);
        var applied = new HashSet<string>(StringComparer.Ordinal);

        foreach (ImportedSlot slot in slots)
        {
            switch (slot.Data)
            {
                case WarehouseData warehouse:
                    ApplyData(store, slot.Key, warehouse, bumpVersion: true);
                    break;
                case CarryConfig carry:
                    ApplyData(store, slot.Key, carry, bumpVersion: false);
                    break;
                case ShopData shop:
                    ApplyData(store, slot.Key, shop, bumpVersion: false);
                    break;
                case ChallengeData challenge:
                    ApplyData(store, slot.Key, challenge, bumpVersion: false);
                    break;
                case ExtractionSettings settings:
                    ApplyData(store, slot.Key, settings, bumpVersion: false);
                    break;
            }

            applied.Add(slot.Key);
        }

        if (mode == ImportMode.Overwrite)
        {
            foreach ((string fileName, string key) in AllSlots)
            {
                if (!applied.Contains(key))
                {
                    ResetSlot(store, fileName, key);
                }
            }
        }

        if (applied.Contains(PendingCarryStore.DataKey))
        {
            PendingCarryStore.RevalidateAgainst(WarehouseStore.Current);
            PendingCarryStore.RevalidateDurability(WarehouseStore.Current);
        }
    }

    /// <summary>Creates a backup of the current save at <c>user://extraction_saves/backups/backup_&lt;timestamp&gt;.zip</c>
    /// (a regular export, so a backup can be restored through the same import flow). Returns the file name, or null on
    /// failure. 在 <c>user://extraction_saves/backups/backup_&lt;时间戳&gt;.zip</c> 生成当前存档的备份（普通导出，因此备份也可通过
    /// 同一导入流程恢复）。返回文件名，失败返回 null。</summary>
    public static string? CreateBackup()
    {
        try
        {
            string dir = Path.Combine(OS.GetUserDataDir(), "extraction_saves", "backups");
            Directory.CreateDirectory(dir);
            string name = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            ExportTo(Path.Combine(dir, name));
            return name;
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"SaveTransfer.CreateBackup: {ex.Message}");
            return null;
        }
    }

    // ----- internals -----

    private static ZipArchive OpenArchive(FileStream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new SaveTransferException(ExtractionLocalization.SaveErrorNotZipText());
        }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static object DeserializeSlot(string fileName, string json) => fileName switch
    {
        "warehouse.json" or "warehouse_nodur.json" => JsonSerializer.Deserialize<WarehouseData>(json, JsonOptions)!,
        "pending_carry.json" => JsonSerializer.Deserialize<CarryConfig>(json, JsonOptions)!,
        "shop.json" => JsonSerializer.Deserialize<ShopData>(json, JsonOptions)!,
        "challenge.json" => JsonSerializer.Deserialize<ChallengeData>(json, JsonOptions)!,
        "settings.json" => JsonSerializer.Deserialize<ExtractionSettings>(json, JsonOptions)!,
        _ => throw new InvalidOperationException($"Unhandled slot file '{fileName}'."),
    };

    /// <summary>Structural validation per slot: valid JSON + an object root + the key fields every version of the data
    /// model has carried. 每槽的结构校验：合法 JSON + 对象根 + 数据模型历代都有的关键字段。</summary>
    private static void ValidateSlotJson(string fileName, string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new SaveTransferException(ExtractionLocalization.SaveErrorBadJsonText(fileName));
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new SaveTransferException(ExtractionLocalization.SaveErrorBadJsonText(fileName));
            }

            switch (fileName)
            {
                case "warehouse.json" or "warehouse_nodur.json" or "pending_carry.json":
                    RequireField(doc, fileName, "Gold", JsonValueKind.Number);
                    RequireField(doc, fileName, "Cards", JsonValueKind.Array);
                    RequireField(doc, fileName, "Relics", JsonValueKind.Array);
                    RequireField(doc, fileName, "Potions", JsonValueKind.Array);
                    break;
                case "shop.json":
                    RequireField(doc, fileName, "Entries", JsonValueKind.Array);
                    break;
                case "challenge.json":
                    RequireField(doc, fileName, "DailyIds", JsonValueKind.Array);
                    RequireField(doc, fileName, "DailyClearCounts", JsonValueKind.Object);
                    RequireField(doc, fileName, "PermanentCleared", JsonValueKind.Array);
                    RequireField(doc, fileName, "PermanentClearCounts", JsonValueKind.Object);
                    break;
            }
        }
    }

    private static void RequireField(JsonDocument doc, string fileName, string field, JsonValueKind kind)
    {
        if (!doc.RootElement.TryGetProperty(field, out JsonElement element) || element.ValueKind != kind)
        {
            throw new SaveTransferException(ExtractionLocalization.SaveErrorMissingFieldText(fileName));
        }
    }

    private static void ApplyData<T>(ModDataStore store, string key, T imported, bool bumpVersion) where T : class, new()
    {
        store.Modify<T>(key, target =>
        {
            CopyInto(target, imported);
            if (bumpVersion && target is WarehouseData warehouse)
            {
                warehouse.Version++;
            }
        });
        store.Save(key);
    }

    private static void ResetSlot(ModDataStore store, string fileName, string key)
    {
        switch (fileName)
        {
            case "warehouse.json" or "warehouse_nodur.json":
                ApplyData(store, key, new WarehouseData(), bumpVersion: true);
                break;
            case "pending_carry.json":
                ApplyData(store, key, new CarryConfig(), bumpVersion: false);
                break;
            case "shop.json":
                ApplyData(store, key, new ShopData(), bumpVersion: false);
                break;
            case "challenge.json":
                ApplyData(store, key, new ChallengeData(), bumpVersion: false);
                break;
            case "settings.json":
                ApplyData(store, key, new ExtractionSettings(), bumpVersion: false);
                break;
        }
    }

    /// <summary>Copies every public settable instance property from <paramref name="source"/> to <paramref name="target"/>
    /// (reference assignment for lists/dicts — the source object is discarded afterwards). The live ModDataStore instance
    /// keeps its identity, so captured references (e.g. a display cache) stay valid.
    /// 把 <paramref name="source"/> 的所有公开可写实例属性拷贝到 <paramref name="target"/>（列表/字典为引用赋值——源对象之后即弃）。
    /// 保持 ModDataStore 活实例身份不变，已捕获的引用（如展示缓存）仍然有效。</summary>
    private static void CopyInto<T>(T target, T source) where T : class
    {
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                property.SetValue(target, property.GetValue(source));
            }
            catch (Exception ex)
            {
                Entry.Logger.Warn($"SaveTransfer.CopyInto: skipped property {typeof(T).Name}.{property.Name}: {ex.Message}");
            }
        }
    }
}
