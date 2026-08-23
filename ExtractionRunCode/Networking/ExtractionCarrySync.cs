using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using ExtractionRun.Data;
using ExtractionRun.Lifecycle;
using ExtractionRun.Modifier;

namespace ExtractionRun.Networking;

/// <summary>
/// Bridges the persistent pending carry config into the lobby-scoped run saved-data, and applies the extraction
/// modifier to a lobby. RitsuLib's lobby staging (<c>SyncLobbyOnChange</c>) pushes each player's contribution to the
/// host on ready and ships it to every machine in the begin-run message, so <c>ExtractionModifier.AfterRunCreated</c>
/// reads the same per-player loadout everywhere.
/// 把待发携带配置暂存进大厅 RunSavedData，并把搜打撤修正项应用到大厅。RitsuLib 大厅暂存负责把每人携带同步到主机并分发给所有机器。
/// </summary>
public static class ExtractionCarrySync
{
    private static StartRunLobby? _registeredLobby;

    /// <summary>Registers the direct carry handoff on the active lobby service.</summary>
    public static void EnsureRegistered(StartRunLobby lobby)
    {
        if (ReferenceEquals(_registeredLobby, lobby))
        {
            return;
        }

        if (_registeredLobby != null)
        {
            _registeredLobby.NetService.UnregisterMessageHandler<ExtractionCarryMessage>(HandleCarryMessage);
        }

        _registeredLobby = lobby;
        lobby.NetService.RegisterMessageHandler<ExtractionCarryMessage>(HandleCarryMessage);
    }

    /// <summary>Stages the local player's persistent pending carry into the lobby staging. 把本机待发携带暂存进大厅。</summary>
    public static void StagePendingCarry(StartRunLobby lobby, ulong localNetId)
    {
        EnsureRegistered(lobby);
        CarryConfig pending = PendingCarryStore.Current;
        ExtractionRunData.Carry.Lobby.Set(lobby, localNetId, pending);
        Entry.Logger.Info($"ExtractionCarrySync staged carry for player {localNetId}: " +
                          $"{pending.Cards.Count} cards, {pending.Relics.Count} relics, " +
                          $"{pending.Potions.Count} potions, {pending.Gold} gold.");
    }

    /// <summary>
    /// Applies the extraction modifier to a lobby (host/singleplayer), carrying the hub-selected challenges as
    /// <c>[SavedProperty] ChallengeIds</c> (a session-only handoff consumed here — LAN rooms inherit it automatically).
    /// Replaces the (normally empty) modifier list and broadcasts <c>LobbyModifiersChangedMessage</c> so every machine's
    /// lobby carries it into the run. 把搜打撤修正项应用到大厅（主机/单机），并把大厅选定的挑战写入 [SavedProperty] ChallengeIds
    /// （会话瞬态在此消费——LAN 房自动继承）。</summary>
    public static void ApplyExtractionModifier(StartRunLobby lobby)
    {
        ModifierModel model = ModelDb.Modifier<ExtractionModifier>().ToMutable();
        if (ExtractionRunContext.PendingChallenges is { Count: > 0 } ids && model is ExtractionModifier modifier)
        {
            ChallengeSelectionResult selection = ChallengeSelectionService.NormalizeRunIds(ids);
            modifier.ChallengeIds = ChallengeSelectionService.SerializeRunIds(selection.Ids);
            modifier.ChallengeCatalogSchemaVersion = ChallengeRegistry.CatalogSchemaVersion;
            modifier.ChallengeCatalogHash = ChallengeRegistry.CatalogHash;
            ExtractionRunContext.PendingChallenges = null;
            if (selection.RejectedIds.Count > 0)
            {
                Entry.Logger.Warn($"ExtractionCarrySync: rejected invalid/duplicate challenge id(s): " +
                                  string.Join(", ", selection.RejectedIds));
            }
            Entry.Logger.Info($"ExtractionCarrySync: applied challenge(s) {string.Join(", ", selection.Ids)} to extraction modifier.");
        }

        lobby.SetModifiers(new List<ModifierModel> { model });
    }

    public static void SendConfirmedCarry(StartRunLobby lobby, CarryConfig config)
    {
        EnsureRegistered(lobby);
        if (lobby.NetService.Type != NetGameType.Client || !lobby.NetService.IsConnected)
        {
            return;
        }

        try
        {
            lobby.NetService.SendMessage(ExtractionCarryMessage.From(config));
            Entry.Logger.Info($"ExtractionCarrySync sent direct carry handoff: {config.Cards.Count} cards, " +
                              $"{config.Relics.Count} relics, {config.Potions.Count} potions, {config.Gold} gold.");
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"ExtractionCarrySync: direct carry handoff failed: {ex.Message}");
        }
    }

    private static void HandleCarryMessage(ExtractionCarryMessage message, ulong senderId)
    {
        StartRunLobby? lobby = _registeredLobby;
        if (lobby == null || lobby.NetService.Type != NetGameType.Host || senderId == lobby.NetService.NetId)
        {
            return;
        }

        try
        {
            CarryConfig carry = message.Decode();
            ExtractionRunData.Carry.Lobby.Set(lobby, senderId, carry);
            Entry.Logger.Info($"ExtractionCarrySync host received direct carry from player {senderId}: " +
                              $"{carry.Cards.Count} cards, {carry.Relics.Count} relics, " +
                              $"{carry.Potions.Count} potions, {carry.Gold} gold.");
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"ExtractionCarrySync: rejected direct carry from player {senderId}: {ex.Message}");
        }
    }

    /// <summary>True when the given modifiers include the extraction modifier. 修正项中是否含搜打撤修正项。</summary>
    public static bool HasExtractionModifier(IEnumerable<ModifierModel> modifiers)
    {
        return modifiers.Any(m => m is ExtractionModifier);
    }
}
