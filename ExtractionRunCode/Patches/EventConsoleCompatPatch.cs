using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Modifier;
using ExtractionRun.Networking;

namespace ExtractionRun.Patches;

/// <summary>
/// The base game's <c>event</c> console command resolves both its lookup and its autocomplete from
/// <see cref="ModelDb.AllEvents"/> (act pools + shared events). ExtractionPointEvent is deliberately never registered
/// into any event pool — a bare subtype, otherwise it would roll as a normal event at random `?` nodes — so the
/// command can't find it (no completion, and even a full id is rejected). This postfix appends the mod's
/// unpooled events to the command's candidate source, so <c>event EXTRACTION_POINT_EVENT</c> completes and summons the
/// extraction point for testing. Gated to extraction runs: outside the mode the event is inert and shouldn't be summonable.
/// 原版 <c>event</c> 指令的查找与补全都来自 ModelDb.AllEvents（各幕事件池 + 共享事件）。ExtractionPointEvent 按设计契约绝不注册进
/// 任何事件池（裸子类型，否则会作为普通事件随机出现在 `?` 节点），因此该指令找不到它（无法补全，手动输入完整 id 也会被拒）。
/// 本补丁向指令的候选源追加本 mod 未入池的事件，让 <c>event EXTRACTION_POINT_EVENT</c> 能补全并召唤撤离点以作测试。
/// 仅限搜打撤局：模式外该事件是惰性的，不应被召唤。
/// </summary>
[HarmonyPatch(typeof(EventConsoleCmd), "get_Events")]
public static class EventConsoleCompatPatch
{
    private static IEnumerable<EventModel> Postfix(IEnumerable<EventModel> __result)
    {
        if (!ExtractionCarrySync.HasExtractionModifier(RunManager.Instance?.State?.Modifiers ?? []) ||
            !ModelDb.Contains(typeof(ExtractionPointEvent)))
        {
            return __result;
        }

        EventModel extraction = ModelDb.Event<ExtractionPointEvent>();
        return __result.Any(e => e.Id == extraction.Id) ? __result : __result.Append(extraction);
    }
}
