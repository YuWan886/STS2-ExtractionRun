using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ExtractionRun.Networking;

/// <summary>
/// Host→client broadcast of the 撤离点 host-authoritative settings (act capacities, gold fee, placement chance).
/// Clients store the received values; host/singleplayer read their local settings directly. The values are locked
/// while a run/lobby is active, so a single snapshot per session is enough.
/// 主机→客机广播撤离点主机权威设置（分幕容量、金币费率、出现概率）。客机存收到的值；主机/单机直接读本地设置。
/// 局内/大厅中设置锁定，故每次会话一份快照足够。
/// </summary>
public struct ExtractionPointSettingsMessage : INetMessage, IPacketSerializable
{
    public int CapacityAct1;
    public int CapacityAct2;
    public int CapacityAct3;
    public int GoldFeeAct1;
    public double GoldFeeRate;
    public double ActChance;

    public bool ShouldBroadcast => false;

    public bool ShouldBuffer => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(CapacityAct1);
        writer.WriteInt(CapacityAct2);
        writer.WriteInt(CapacityAct3);
        writer.WriteInt(GoldFeeAct1);
        writer.WriteDouble(GoldFeeRate);
        writer.WriteDouble(ActChance);
    }

    public void Deserialize(PacketReader reader)
    {
        CapacityAct1 = reader.ReadInt();
        CapacityAct2 = reader.ReadInt();
        CapacityAct3 = reader.ReadInt();
        GoldFeeAct1 = reader.ReadInt();
        GoldFeeRate = reader.ReadDouble();
        ActChance = reader.ReadDouble();
    }
}

/// <summary>
/// Client→host request for the host's 撤离点 settings. Sent when a client joins an extraction lobby (the host may
/// have broadcast the settings before the client connected). The host replies with an <see cref="ExtractionPointSettingsMessage"/>.
/// 客机→主机请求撤离点设置：客机加入搜打撤大厅时发送（主机可能在客机连入前已广播设置）。主机以设置消息应答。
/// </summary>
public struct RequestExtractionPointSettingsMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;

    public bool ShouldBuffer => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
    }
}

/// <summary>
/// Broadcast announcing that one player confirmed their 撤离点 panel. Sent by the machine whose local player just
/// confirmed (ShouldBroadcast echoes a client's message to every peer), so each machine can wait until ALL players
/// have confirmed before ending the run — the shared-event option-task barrier only waits for the local machine's
/// copies, not for a remote player's human input.
/// 广播某玩家已确认撤离面板：本地玩家确认后由本机发出（ShouldBroadcast 把客机消息回显给所有机器），各机据此等待全队确认
/// 再结束跑局——共享事件的选项任务屏障只等本机副本，等不到远端玩家的真实操作。
/// </summary>
public struct ExtractionPointSelectionConfirmedMessage : INetMessage, IPacketSerializable
{
    public ulong PlayerId;

    public bool ShouldBroadcast => true;

    public bool ShouldBuffer => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerId);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerId = reader.ReadULong();
    }
}
