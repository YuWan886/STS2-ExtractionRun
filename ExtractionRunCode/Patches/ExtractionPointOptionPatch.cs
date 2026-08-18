using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using ExtractionRun.Modifier;

namespace ExtractionRun.Patches;

/// <summary>
/// The two extraction options (普通撤离 / 金币撤离) are marked with <c>ThatWillKillPlayerIf(_ => true)</c> so they flash
/// red — ending the run is the whole point of the option. But the vanilla button treats a "will kill" option in
/// multiplayer as an accidental suicide: <c>NEventOptionButton.OnRelease</c> swallows the click (a death-prevention
/// thought bubble) and never calls <c>OptionButtonClicked</c>, so in MP the extraction options were unselectable.
/// This prefix hands the extraction options straight to the selection handler on machines where the vanilla guard
/// would block; the red glow (applied via <c>WillKillPlayer</c> in _Ready/OnFocus) is left untouched.
/// 两个撤离选项用 ThatWillKillPlayerIf(_ => true) 标红——终结跑局本就是选项的目的。但原版按钮把多人下的“致死”选项当作
/// 误自杀：NEventOptionButton.OnRelease 会吞掉点击（弹出防死亡气泡）而不调用 OptionButtonClicked，导致多人局撤离选项
/// 无法选择。此前置补丁在原版拦截生效的机器上把撤离选项直接交给选择处理；红色警示光效（_Ready/OnFocus 经 WillKillPlayer
/// 施加）不受影响。
/// </summary>
[HarmonyPatch(typeof(NEventOptionButton), "OnRelease")]
public static class ExtractionPointKillOptionPatch
{
    private static bool Prefix(NEventOptionButton __instance)
    {
        EventOption? option = __instance.Option;
        // Only the two extraction options carry a kill predicate — 路过 and the locked (unaffordable) gold option
        // don't, and every other event is vanilla. In singleplayer the vanilla guard doesn't trigger either, so the
        // vanilla path is fine there.
        // 仅两个撤离选项带 kill 谓词（路过/锁定的金币选项没有）；单机下原版拦截不生效，原路径即可。
        if (option?.WillKillPlayer == null
            || __instance.Event is not ExtractionPointEvent evt
            || evt.Owner?.RunState.Players.Count <= 1)
        {
            return true;
        }

        // The vanilla MP guard would swallow this click — dispatch the selection directly, using the option's index
        // in CurrentOptions (the order the buttons were built in), the same value the vanilla OnRelease would pass.
        // 原版多人防死亡拦截会吞掉这次点击——直接用 CurrentOptions 里该选项的索引（即按钮构建顺序，与原版 OnRelease
        // 将传入的值一致）完成选择。
        IReadOnlyList<EventOption> options = evt.CurrentOptions;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] == option)
            {
                NEventRoom.Instance?.OptionButtonClicked(option, i);
                return false;
            }
        }

        return true; // Option not in CurrentOptions (shouldn't happen) — let vanilla run.
    }
}
