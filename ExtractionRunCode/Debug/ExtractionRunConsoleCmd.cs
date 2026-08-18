using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using ExtractionRun.Data;
using ExtractionRun.UI;

namespace ExtractionRun.Debug;

/// <summary>
/// 搜打撤仓库调试指令：<c>extraction reset</c>（带确认弹窗）、<c>extraction add/remove card|relic|potion|gold</c>（支持数量）。
/// Auto-discovered via <c>AbstractConsoleCmd</c> subtypes (no registration). Local-only (the warehouse is profile-scoped).
/// </summary>
public sealed class ExtractionRunConsoleCmd : AbstractConsoleCmd
{
    private static readonly string[] RootCommands = { "reset", "add", "remove", "refresh" };
    private static readonly string[] TypeCommands = { "card", "relic", "potion", "gold" };

    /// <summary>
    /// Console add/remove quantity cap — a sanity guard. The hub's MaxTileKinds only bounds rendering, not the size of
    /// the serialized warehouse, so an unbounded count could bloat the save. 增删数量上限（防呆）：渲染有 MaxTileKinds 兜底，
    /// 但序列化体积没有，故上限防止把存档撑爆。
    /// </summary>
    private const int MaxAddRemoveCount = 999;

    public override string CmdName => "extraction";

    public override string Args =>
        "reset | refresh | add <card|relic|potion|gold> <id|amount> [count] | remove <card|relic|potion|gold> <id|amount> [count]";

    public override string Description => "搜打撤仓库调试：重置仓库（需确认）、刷新每日挑战，或增删卡牌/遗物/药水/金币。";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        return args[0].ToLowerInvariant() switch
        {
            "reset" => ProcessReset(),
            "refresh" => ProcessRefresh(),
            "add" => ProcessAdd(args),
            "remove" => ProcessRemove(args),
            _ => Usage(),
        };
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
        {
            return CompleteArgument(RootCommands, [], args.Length == 0 ? string.Empty : args[0], CompletionType.Subcommand);
        }

        string root = args[0].ToLowerInvariant();
        if (root is not ("add" or "remove"))
        {
            return base.GetArgumentCompletions(player, args);
        }

        if (args.Length == 2)
        {
            return CompleteArgument(TypeCommands, [args[0]], args[1], CompletionType.Argument);
        }

        string type = args[1].ToLowerInvariant();
        if (type == "gold" || args.Length > 3)
        {
            // Gold takes an amount, count is a plain integer — nothing to complete. 金币是数量、count 是整数，无需补全。
            return base.GetArgumentCompletions(player, args);
        }

        string[] completed = args.Take(2).ToArray();
        return type switch
        {
            "card" => CompleteArgument(ModelDb.AllCards.Select(c => c.Id.Entry).Distinct(), completed, args[2]),
            "relic" => CompleteArgument(ModelDb.AllRelics.Select(r => r.Id.Entry).Distinct(), completed, args[2]),
            "potion" => CompleteArgument(ModelDb.AllPotions.Select(p => p.Id.Entry).Distinct(), completed, args[2]),
            _ => base.GetArgumentCompletions(player, args),
        };
    }

    // ----- reset 重置 -----

    private CmdResult ProcessReset()
    {
        if (IsRunOrLobbyActive())
        {
            return new CmdResult(false, "进行中的跑局/大厅里不能重置仓库（携带已暂存）。");
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            return new CmdResult(false, "界面未就绪（NGame 不可用）。");
        }

        game.AddChild(new ExtractionConfirmDialog(
            ExtractionLocalization.ConfirmResetHeaderText(),
            ExtractionLocalization.ConfirmResetBodyText(),
            ConfirmReset));
        return new CmdResult(success: true, "已弹出确认框：确定后将清空仓库并重新发放初始物品。");
    }

    private static void ConfirmReset()
    {
        WarehouseStore.Reset();
        PendingCarryStore.Clear();
        WarehouseHubScreen.Current?.RefreshForExternalMutationAfterShrink();
        Entry.Logger.Info("ExtractionRunConsoleCmd: warehouse reset to starter items.");
    }

    // ----- refresh 刷新每日挑战 -----

    private CmdResult ProcessRefresh()
    {
        ChallengeStore.RefreshDaily();

        // A still-selected daily that fell out of the new pool is dropped from the draft, so a run never carries an
        // id that isn't on offer (the centralized selection service rejects it). 把被换出池子的已选每日从草稿移除，
        // 开跑不会带一个不在池中的 id（选择服务会拒绝它）。
        WarehouseHubScreen.Current?.RemovePendingChallengesNotInDailyPool();
        WarehouseHubScreen.Current?.RefreshForExternalMutation();
        return new CmdResult(true, "已刷新每日挑战。");
    }

    // ----- add 添加 -----

    private CmdResult ProcessAdd(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage();
        }

        string type = args[1].ToLowerInvariant();
        if (type == "gold")
        {
            return AddGold(args);
        }

        if (args.Length < 3)
        {
            return new CmdResult(false, $"用法：extraction add {type} <id> [count]");
        }

        int count = ParseCount(args);
        if (count < 0)
        {
            return new CmdResult(false, $"数量需为 1~{MaxAddRemoveCount}。");
        }

        switch (type)
        {
            case "card":
            {
                CardModel? card = FindCard(args[2]);
                if (card == null)
                {
                    return new CmdResult(false, $"找不到卡牌 '{args[2]}'。");
                }

                List<WarehouseCard> cards = Enumerable.Range(0, count)
                    .Select(_ => new WarehouseCard
                    {
                        Card = card.ToMutable().ToSerializable(),
                        Durability = WarehouseStore.MaxDurabilityForCard(card.Id),
                    }).ToList();
                WarehouseStore.Deposit(cards, null, null, 0);
                WarehouseHubScreen.Current?.RefreshForExternalMutation();
                return new CmdResult(true, $"已向仓库添加 {count} 张 {card.Id.Entry}。");
            }
            case "relic":
            {
                RelicModel? relic = FindRelic(args[2]);
                if (relic == null)
                {
                    return new CmdResult(false, $"找不到遗物 '{args[2]}'。");
                }

                List<WarehouseRelic> relics = Enumerable.Range(0, count)
                    .Select(_ => new WarehouseRelic
                    {
                        Relic = relic.ToMutable().ToSerializable(),
                        Durability = WarehouseStore.MaxDurabilityForRelic(),
                    }).ToList();
                WarehouseStore.Deposit(null, relics, null, 0);
                WarehouseHubScreen.Current?.RefreshForExternalMutation();
                return new CmdResult(true, $"已向仓库添加 {count} 个 {relic.Id.Entry}。");
            }
            case "potion":
            {
                PotionModel? potion = FindPotion(args[2]);
                if (potion == null)
                {
                    return new CmdResult(false, $"找不到药水 '{args[2]}'。");
                }

                List<SerializablePotion> potions = Enumerable.Range(0, count)
                    .Select(_ => potion.ToMutable().ToSerializable(0)).ToList();
                WarehouseStore.Deposit(null, null, potions, 0);
                WarehouseHubScreen.Current?.RefreshForExternalMutation();
                return new CmdResult(true, $"已向仓库添加 {count} 瓶 {potion.Id.Entry}。");
            }
            default:
                return new CmdResult(false, "类型须为 card | relic | potion | gold。");
        }
    }

    private static CmdResult AddGold(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], out int amount) || amount <= 0)
        {
            return new CmdResult(false, "用法：extraction add gold <数量>");
        }

        int room = WarehouseStore.MaxGold - WarehouseStore.Current.Gold;
        if (room <= 0)
        {
            return new CmdResult(false, $"仓库金币已达上限（{WarehouseStore.MaxGold}）。");
        }

        int added = Math.Min(amount, room);
        WarehouseStore.Deposit(null, null, null, added);
        WarehouseHubScreen.Current?.RefreshForExternalMutation();
        return new CmdResult(true, $"已向仓库添加 {added} 金币（当前 {WarehouseStore.Current.Gold}）。");
    }

    // ----- remove 删除 -----

    private CmdResult ProcessRemove(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage();
        }

        if (IsRunOrLobbyActive())
        {
            return new CmdResult(false, "进行中的跑局/大厅里不能删除仓库物品（携带已暂存）。");
        }

        string type = args[1].ToLowerInvariant();
        if (type == "gold")
        {
            return RemoveGold(args);
        }

        if (args.Length < 3)
        {
            return new CmdResult(false, $"用法：extraction remove {type} <id> [count]");
        }

        int count = ParseCount(args);
        if (count < 0)
        {
            return new CmdResult(false, $"数量需为 1~{MaxAddRemoveCount}。");
        }

        int removed;
        string label;
        switch (type)
        {
            case "card":
            {
                CardModel? card = FindCard(args[2]);
                if (card == null)
                {
                    return new CmdResult(false, $"找不到卡牌 '{args[2]}'。");
                }

                removed = WarehouseStore.RemoveCards(card.Id, count);
                label = card.Id.Entry;
                break;
            }
            case "relic":
            {
                RelicModel? relic = FindRelic(args[2]);
                if (relic == null)
                {
                    return new CmdResult(false, $"找不到遗物 '{args[2]}'。");
                }

                removed = WarehouseStore.RemoveRelics(relic.Id, count);
                label = relic.Id.Entry;
                break;
            }
            case "potion":
            {
                PotionModel? potion = FindPotion(args[2]);
                if (potion == null)
                {
                    return new CmdResult(false, $"找不到药水 '{args[2]}'。");
                }

                removed = WarehouseStore.RemovePotions(potion.Id, count);
                label = potion.Id.Entry;
                break;
            }
            default:
                return new CmdResult(false, "类型须为 card | relic | potion | gold。");
        }

        // Strip the same ids out of the pending carry, else a staged copy that no longer exists in the warehouse
        // would be injected at run start while ConsumeCarried skips the missing stock — free items. Remove re-maps
        // carried durability to the remaining (lowest-first) copies so the deposit decrements the right value.
        // 同步把被删物品从携带中剥离（含金币 clamp），否则开跑时会净增免费物品；并重映射携带耐久到剩余副本（最低优先），
        // 保证撤离时按正确值递减。
        PendingCarryStore.RevalidateAgainst(WarehouseStore.Current);
        PendingCarryStore.RevalidateDurability(WarehouseStore.Current);
        WarehouseHubScreen.Current?.RefreshForExternalMutationAfterShrink();
        return new CmdResult(true, $"已从仓库移除 {removed} 个 {label}。");
    }

    private static CmdResult RemoveGold(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[2], out int amount) || amount <= 0)
        {
            return new CmdResult(false, "用法：extraction remove gold <数量>");
        }

        int removed = Math.Min(amount, WarehouseStore.Current.Gold);
        WarehouseStore.RemoveGold(removed);
        PendingCarryStore.RevalidateAgainst(WarehouseStore.Current);
        WarehouseHubScreen.Current?.RefreshForExternalMutationAfterShrink();
        return new CmdResult(true, $"已从仓库移除 {removed} 金币（当前 {WarehouseStore.Current.Gold}）。");
    }

    // ----- Helpers -----

    private static int ParseCount(string[] args)
    {
        if (args.Length >= 4 && int.TryParse(args[3], out int count) && count is >= 1 and <= MaxAddRemoveCount)
        {
            return count;
        }

        // Default 1 when the count is omitted; a malformed explicit count is an error (return -1). 省略时默认 1，非法显式数量报错。
        return args.Length >= 4 ? -1 : 1;
    }

    private static CardModel? FindCard(string id)
    {
        string entry = id.ToUpperInvariant();
        return ModelDb.AllCards.FirstOrDefault(c => c.Id.Entry == entry);
    }

    private static RelicModel? FindRelic(string id)
    {
        string entry = id.ToUpperInvariant();
        return ModelDb.AllRelics.FirstOrDefault(r => r.Id.Entry == entry);
    }

    private static PotionModel? FindPotion(string id)
    {
        string entry = id.ToUpperInvariant();
        return ModelDb.AllPotions.FirstOrDefault(p => p.Id.Entry == entry);
    }

    /// <summary>
    /// Blocks reset/remove while a run is in progress or a character-select lobby is open. The lobby already staged the
    /// pending carry into run saved-data, which a console reset/remove cannot retract — without this, a staged copy of an
    /// item the reset just wiped would be injected at run start (free-item dupe). 跑局/角色选择大厅中禁止重置/删仓：
    /// 大厅已暂存携带，删仓够不到那份拷贝，开跑会净增。
    /// </summary>
    private static bool IsRunOrLobbyActive()
    {
        if (RunManager.Instance?.IsInProgress == true)
        {
            return true;
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is NCharacterSelectScreen;
    }

    private static CmdResult Usage() =>
        new(false, "用法：extraction reset | add <card|relic|potion|gold> <id|数量> [count] | remove <card|relic|potion|gold> <id|数量> [count]");
}
