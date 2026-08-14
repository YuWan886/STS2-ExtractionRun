using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using ExtractionRun.Modifier;
using ExtractionRun.Settings;

namespace ExtractionRun.Networking;

/// <summary>
/// Host-authoritative 撤离点 settings: the three act capacities (普通撤离), the gold-fee base + per-act compounding rate
/// (金币撤离), and the per-act placement chance. Host and singleplayer read the local settings POCO directly; a
/// multiplayer client stores the snapshot the host broadcast and enforces those numbers. Values are locked while a
/// run/lobby is active, so one snapshot per session is enough.
/// 撤离点主机权威设置：三个分幕容量（普通撤离）、金币费基数与每幕复利（金币撤离）、每幕出现概率。主机/单机直接读本地设置
/// POCO；多人客机存主机广播的快照并按该值执行。局内/大厅中锁定，每次会话一份快照即可。
/// </summary>
public readonly struct ExtractionPointSettingsSnapshot
{
    public readonly int CapacityAct1;
    public readonly int CapacityAct2;
    public readonly int CapacityAct3;
    public readonly int GoldFeeAct1;
    public readonly double GoldFeeRate;
    public readonly double ActChance;

    public ExtractionPointSettingsSnapshot(int capacityAct1, int capacityAct2, int capacityAct3, int goldFeeAct1,
        double goldFeeRate, double actChance)
    {
        CapacityAct1 = capacityAct1;
        CapacityAct2 = capacityAct2;
        CapacityAct3 = capacityAct3;
        GoldFeeAct1 = goldFeeAct1;
        GoldFeeRate = goldFeeRate;
        ActChance = actChance;
    }

    public static ExtractionPointSettingsSnapshot FromSettings(ExtractionSettings s) => new(
        Math.Max(0, s.ExtractionPointCapacityAct1),
        Math.Max(0, s.ExtractionPointCapacityAct2),
        Math.Max(0, s.ExtractionPointCapacityAct3),
        Math.Max(0, s.ExtractionPointGoldFeeAct1),
        Math.Max(0, s.ExtractionPointGoldFeeRate),
        Math.Clamp(s.ExtractionPointActChance, 0, 1));

    /// <summary>普通撤离 capacity for the given act index (0-based: act 1 → act1 value, act 2 → act2, act 3+ → act3).</summary>
    public int CapacityForAct(int actIndex) => actIndex switch
    {
        0 => CapacityAct1,
        1 => CapacityAct2,
        _ => CapacityAct3,
    };

    /// <summary>金币撤离 fee for the given act index — the base fee compounded by the rate once per act past act 1.</summary>
    public int GoldFeeForAct(int actIndex) =>
        Math.Max(0, (int)Math.Round(GoldFeeAct1 * Math.Pow(1 + GoldFeeRate, Math.Max(0, actIndex))));
}

/// <summary>
/// Carries the host-authoritative 撤离点 settings between machines and resolves the active values on every peer.
/// The message handlers are registered on the lobby/run net service on first use and re-registered when the service
/// instance changes (AwcSpire-style <c>EnsureRegistered</c>). The host broadcasts on extraction-lobby apply; a client
/// that joined after the broadcast requests a copy.
/// 撤离点主机权威设置在机器间的搬运与取值。消息处理句柄在首次使用时按 net service 实例注册、实例变化时重注册
/// （AwcSpire 式 EnsureRegistered）。主机在应用搜打撤修正项时广播；广播后加入的客机主动请求一份。
/// </summary>
public static class ExtractionPointSettingsSync
{
    /// <summary>True once a client has received the host's snapshot. Host/SP never set this (they read local settings).</summary>
    public static bool HasHostSettings { get; private set; }

    /// <summary>The host's snapshot, as received by a client. Host/SP ignore it.</summary>
    public static ExtractionPointSettingsSnapshot HostSettings { get; private set; }

    private static INetGameService? _registeredNetService;

    /// <summary>Resolved settings for the local machine: a client uses the host snapshot, host/SP the local POCO.</summary>
    public static ExtractionPointSettingsSnapshot Current =>
        IsClientWithHostSettings ? HostSettings : ExtractionPointSettingsSnapshot.FromSettings(ExtractionSettingsPage.Current);

    private static bool IsClientWithHostSettings =>
        RunManager.Instance?.NetService?.Type == NetGameType.Client && HasHostSettings;

    public static void EnsureRegistered(INetGameService netService)
    {
        if (_registeredNetService == netService)
        {
            return;
        }

        if (_registeredNetService != null)
        {
            _registeredNetService.UnregisterMessageHandler<ExtractionPointSettingsMessage>(HandleSettingsMessage);
            _registeredNetService.UnregisterMessageHandler<RequestExtractionPointSettingsMessage>(HandleRequestMessage);
            _registeredNetService.UnregisterMessageHandler<ExtractionPointSelectionConfirmedMessage>(HandleSelectionConfirmedMessage);
        }

        _registeredNetService = netService;
        if (netService != null)
        {
            netService.RegisterMessageHandler<ExtractionPointSettingsMessage>(HandleSettingsMessage);
            netService.RegisterMessageHandler<RequestExtractionPointSettingsMessage>(HandleRequestMessage);
            netService.RegisterMessageHandler<ExtractionPointSelectionConfirmedMessage>(HandleSelectionConfirmedMessage);
        }
    }

    /// <summary>
    /// Broadcasts that the local player confirmed their 撤离点 panel. SP no-ops (the barrier trivially completes with
    /// the single local player). Re-resolves the net service if the lobby-time registration went stale (the run's
    /// service may differ across sessions). 广播本机玩家已确认撤离面板；单机无需广播（唯一玩家即全队）。若大厅期注册的 net
    /// service 已变化（跨会话），此处重新解析并注册。
    /// </summary>
    public static void SendSelectionConfirmed(ulong playerId)
    {
        INetGameService? netService = _registeredNetService;
        if (netService is not { IsConnected: true })
        {
            netService = RunManager.Instance?.NetService;
            if (netService != null)
            {
                EnsureRegistered(netService);
            }
        }

        if (netService is not { IsConnected: true, Type: NetGameType.Host or NetGameType.Client })
        {
            return;
        }

        netService.SendMessage(new ExtractionPointSelectionConfirmedMessage { PlayerId = playerId });
    }

    /// <summary>Host/SP value resolver — the capacity for 普通撤离 in the given act. 普通撤离的分幕容量取值。</summary>
    public static int CapacityForAct(int actIndex) => Current.CapacityForAct(actIndex);

    /// <summary>Host/SP value resolver — the gold fee for 金币撤离 in the given act. 金币撤离的分幕费用取值。</summary>
    public static int GoldFeeForAct(int actIndex) => Current.GoldFeeForAct(actIndex);

    /// <summary>Host/SP value resolver — the per-act placement chance (0–1). 每幕放置概率取值。</summary>
    public static double ActChance => Current.ActChance;

    /// <summary>
    /// Host broadcasts the current settings to all clients. Called when the extraction modifier is applied to the lobby
    /// (all clients are connected by then — the run can't start until every player readies). Registered handlers are
    /// installed lazily via <see cref="EnsureRegistered"/>.
    /// 主机向所有客机广播当前设置。在搜打撤修正项应用到大厅时调用（此时所有客机已连入——全员就绪才能开跑）。
    /// </summary>
    public static void BroadcastSettings(INetGameService netService)
    {
        EnsureRegistered(netService);
        if (netService is not { IsConnected: true, Type: NetGameType.Host })
        {
            return;
        }

        ExtractionPointSettingsMessage message = new()
        {
            CapacityAct1 = ExtractionSettingsPage.Current.ExtractionPointCapacityAct1,
            CapacityAct2 = ExtractionSettingsPage.Current.ExtractionPointCapacityAct2,
            CapacityAct3 = ExtractionSettingsPage.Current.ExtractionPointCapacityAct3,
            GoldFeeAct1 = ExtractionSettingsPage.Current.ExtractionPointGoldFeeAct1,
            GoldFeeRate = ExtractionSettingsPage.Current.ExtractionPointGoldFeeRate,
            ActChance = ExtractionSettingsPage.Current.ExtractionPointActChance,
        };
        netService.SendMessage(message);
        Entry.Logger.Info("ExtractionPointSettingsSync: broadcast host settings to clients.");
    }

    /// <summary>Client asks the host for its settings (sent on joining an extraction lobby after the initial broadcast).</summary>
    public static void RequestFromHost(INetGameService netService)
    {
        EnsureRegistered(netService);
        if (netService is not { IsConnected: true, Type: NetGameType.Client })
        {
            return;
        }

        netService.SendMessage(default(RequestExtractionPointSettingsMessage));
        Entry.Logger.Info("ExtractionPointSettingsSync: requested host settings.");
    }

    private static void HandleSettingsMessage(ExtractionPointSettingsMessage message, ulong senderId)
    {
        HostSettings = new ExtractionPointSettingsSnapshot(
            message.CapacityAct1, message.CapacityAct2, message.CapacityAct3,
            message.GoldFeeAct1, message.GoldFeeRate, message.ActChance);
        HasHostSettings = true;
        Entry.Logger.Info("ExtractionPointSettingsSync: stored host settings.");
    }

    private static void HandleRequestMessage(RequestExtractionPointSettingsMessage message, ulong senderId)
    {
        if (_registeredNetService is { Type: NetGameType.Host })
        {
            BroadcastSettings(_registeredNetService);
        }
    }

    private static void HandleSelectionConfirmedMessage(ExtractionPointSelectionConfirmedMessage message, ulong senderId)
    {
        ExtractionPointFlow.HandleRemoteConfirmed(message.PlayerId);
    }
}
