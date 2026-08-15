using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Networking;
using ExtractionRun.UI;

namespace ExtractionRun.Modifier;

/// <summary>
/// 撤离点 (extraction point) event — the Tarkov-style escape node. A shared event (vanilla shared-vote semantics: the
/// top-voted option executes for every player), placed by <see cref="ExtractionModifier"/> as a special-icon map node.
/// Three options:
/// <list type="bullet">
/// <item>普通撤离 — capacity-limited carry-out (per-act rarity-weight slots, potions/gold free); opens a per-player
/// selection panel and ends the run as a defeat once every player confirms.</item>
/// <item>金币撤离 — pay a per-act gold fee to carry EVERYTHING; a player who can't afford it (but the vote still wins)
/// degrades to 普通撤离 rather than breaking the extraction.</item>
/// <item>路过 — nobody extracts, the run continues; the run's one extraction point is spent.</item>
/// </list>
/// Extraction is a third settlement state: durability decrements like a victory but the run counts as a defeat — no
/// clear reward, no victory epoch. See <c>ExtractionPointFlow</c> (the confirm barrier) and <c>ExtractionRunEnd</c>.
/// 撤离点事件：由 ExtractionModifier 放置为特殊图标地图节点的共享事件。三选项：
/// 普通撤离（分幕容量制带出，药水/金币免费，弹每人选择面板，全员确认后以失败结算）；金币撤离（付分幕金币费全带，付不起者
/// 降级为普通撤离）；路过（无人撤离，跑局继续，本局唯一撤离点消耗）。
/// </summary>
public sealed class ExtractionPointEvent : EventModel
{
    public override bool IsShared => true;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        Player me = Owner!;
        int capacity = ExtractionPointSettingsSync.CapacityForAct(me.RunState.CurrentActIndex);
        int fee = ExtractionPointSettingsSync.GoldFeeForAct(me.RunState.CurrentActIndex);
        bool canAfford = me.Gold >= fee;

        // Both extraction options end the run (kill the whole party) — flash them red so that consequence is legible.
        // 两个撤离选项都会终结跑局（杀死全队）——标红让后果可见。
        // Surface the act capacity / gold fee in the option descriptions so the cost is legible before the panel opens.
        // 在选项描述里直接显示分幕容量与金币费，打开面板前就能看到代价。
        EventOption normal = new EventOption(this, OnNormalExtract, InitialOptionKey("EXTRACT"))
            .ThatWillKillPlayerIf(_ => true);
        normal.Description.Add("Capacity", capacity);

        EventOption gold;
        if (canAfford)
        {
            gold = new EventOption(this, OnGoldExtract, InitialOptionKey("GOLD_EXTRACT"))
                .ThatWillKillPlayerIf(_ => true);
        }
        else
        {
            // Can't afford the fee → option is locked (OnChosen == null), so this player can't VOTE for it. If it
            // still wins via the majority, OnGoldExtract's degrade path below keeps this player playable.
            // 付不起费用 → 选项锁定（OnChosen == null），此玩家投不了它；若仍被多数票选中，OnGoldExtract 的降级路径兜底。
            gold = new EventOption(this, null, InitialOptionKey("GOLD_EXTRACT"));
        }
        gold.Description.Add("Fee", fee);

        return new EventOption[]
        {
            normal,
            gold,
            new EventOption(this, OnPass, InitialOptionKey("PASS")),
        };
    }

    private async Task OnNormalExtract()
    {
        Player me = Owner!;
        // The option runs on every player's event instance on this machine; only the LOCAL player's machine opens a
        // panel and drives the flow — remote copies are no-ops so each machine waits for its own player's input.
        // 选项在本机所有玩家的副本上都会执行；只有本地玩家打开面板并驱动流程——远端副本为 no-op，各机只等自己玩家操作。
        if (!LocalContext.IsMe(me))
        {
            return;
        }

        ExtractionPointSelection? selection = await ExtractionPointPanel.ShowAndWait(this);
        if (selection == null)
        {
            return;
        }

        await ConfirmAndEndRun(me, selection);
    }

    private async Task OnGoldExtract()
    {
        Player me = Owner!;
        if (!LocalContext.IsMe(me))
        {
            return;
        }

        int fee = ExtractionPointSettingsSync.GoldFeeForAct(me.RunState.CurrentActIndex);
        if (me.Gold < fee)
        {
            await OnNormalExtract();
            return;
        }

        await ConfirmAndEndRun(me, new ExtractionPointSelection { Kind = ExtractionPointKind.Gold, GoldFee = fee });
    }

    private Task OnPass()
    {
        SetEventFinished(L10NLookup("EXTRACTION_POINT_EVENT.pages.PASS.description"));
        return Task.CompletedTask;
    }

    private static async Task ConfirmAndEndRun(Player me, ExtractionPointSelection selection)
    {
        ExtractionPointFlow.NotifyLocalConfirmed(me.NetId, selection);

        // The shared-event option-task barrier only waits for this machine's copies — a remote player's human panel
        // input isn't tracked by it. Wait for every machine to confirm before ending the run, or machine A hits the
        // game-over while teammate B is still picking. Show the vanilla waiting overlay while we wait.
        // 共享事件屏障只等本机副本——远端玩家的真实操作它等不到。等全队确认再结束，避免 A 机进结算而队友 B 还在挑牌。
        // 等待期间显示原版等待覆盖层。
        ExtractionPointWaitingOverlay? waiting = ExtractionPointWaitingOverlay.ShowIfWaiting(me.RunState.Players);
        try
        {
            await ExtractionPointFlow.WaitForAllConfirmed(me.RunState.Players);
        }
        finally
        {
            waiting?.Close();
        }

        await EndRunAsExtraction(me.RunState);
    }

    /// <summary>
    /// Ends the run as a defeat via the abandon-style forced kill (bypasses Fairy-in-a-Bottle). When every player is
    /// dead, <c>CreatureCmd.Kill</c> calls <c>OnEnded(false)</c>, which fires <c>RunEndedEvent</c> — the settlement
    /// hook <c>ExtractionRunEnd</c> then deposits the recorded selection (third state). Each machine runs this after
    /// the all-confirm barrier, so the death lands everywhere at once.
    /// 用放弃式的强制击杀结束跑局（绕过复活药水）。全队死亡时 CreatureCmd.Kill 调用 OnEnded(false)，触发 RunEndedEvent——
    /// 结算钩子 ExtractionRunEnd 据此入仓记录的选择（第三态）。各机在全队确认后各自执行，死亡同时落地。
    /// </summary>
    private static async Task EndRunAsExtraction(IRunState runState)
    {
        var creatures = runState.Players.Select(p => p.Creature).ToList();
        await CreatureCmd.Kill(creatures, force: true);
    }
}
