using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using ExtractionRun.Data;

namespace ExtractionRun.UI;

/// <summary>
/// The 挑战 hub page (partial of <see cref="WarehouseHubScreen"/>): the day's daily challenges + the permanent list,
/// multi-select via toggle tiles. Selection is a hub-global session draft (<c>_pendingChallenges</c>) that rides into
/// the run at StartRun; selecting a challenge live-clamps the carry draft and greys the warehouse tiles (see
/// <c>ClampDraftToChallenges</c>/<c>CanCarryCardTile</c>). Clear counts are shown on every challenge card; daily
/// challenges remain selectable after a clear. 挑战页：每日挑战（当日 3 个）+ 常驻列表，多选切换。选择为会话级草稿，开跑时随行；
/// 选定即实时钳制携带草稿并灰化仓库瓦片。每张挑战卡均显示累计通关次数；每日挑战通关后仍可选择。
/// </summary>
public sealed partial class WarehouseHubScreen
{
    private enum ChallengeEntryLayout { Daily, Permanent }

    private LineEdit _challengeSearchEdit = null!;
    private OptionButton _challengeTagFilter = null!;

    private Control BuildChallengePage()
    {
        var page = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        page.AddThemeConstantOverride("separation", 16);

        page.AddChild(BuildChallengeFilterBar());

        var columns = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", 24);
        columns.AddChild(BuildDailyChallengeColumn());
        columns.AddChild(BuildPermanentChallengeColumn());
        page.AddChild(columns);

        var hint = MakeLabel("");
        hint.AddThemeColorOverride("font_color", ExtractionTheme.TextSecondary);
        hint.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeBody);
        page.AddChild(hint);
        _challengeHintLabel = hint;

        return page;
    }

    private Control BuildChallengeFilterBar()
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);

        _challengeSearchEdit = new LineEdit
        {
            PlaceholderText = ExtractionLocalization.ChallengeSearchPlaceholderText(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(260f, 38f),
        };
        _challengeSearchEdit.TextChanged += _ => RefreshChallengePage();
        row.AddChild(_challengeSearchEdit);

        _challengeTagFilter = new OptionButton
        {
            CustomMinimumSize = new Vector2(150f, 38f),
        };
        _challengeTagFilter.AddItem(ExtractionLocalization.ChallengeFilterAllText());
        foreach (ChallengeTag tag in Enum.GetValues<ChallengeTag>())
        {
            _challengeTagFilter.AddItem(ExtractionLocalization.ChallengeTagText(tag));
        }
        _challengeTagFilter.ItemSelected += _ => RefreshChallengePage();
        row.AddChild(_challengeTagFilter);
        return row;
    }

    /// <summary>Left-side daily challenges: larger vertical cards for the day's three selectable runs.
    /// 左侧每日挑战：以更醒目的纵向大卡片承载当天三个可选挑战。</summary>
    private PanelContainer BuildDailyChallengeColumn()
    {
        PanelContainer column = MakeCard(stretchRatio: 3f, out VBoxContainer body);
        column.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var header = MakeLabel(ExtractionLocalization.ChallengeSectionDailyText());
        header.AddThemeFontOverride("font", ExtractionTheme.Bold);
        header.AddThemeFontSizeOverride("font_size", 30);
        header.AddThemeColorOverride("font_color", ExtractionTheme.PrimaryHover);
        body.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _dailyChallengeList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _dailyChallengeList.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(_dailyChallengeList);
        body.AddChild(scroll);
        return column;
    }

    /// <summary>Right-side permanent challenges: a full-width vertical list for comfortable reading and selection.
    /// 右侧常驻挑战：全宽纵向列表，保证文字阅读与选择都舒适。</summary>
    private PanelContainer BuildPermanentChallengeColumn()
    {
        PanelContainer column = MakeCard(stretchRatio: 2f, out VBoxContainer body);
        column.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var header = MakeLabel(ExtractionLocalization.ChallengeSectionPermanentText());
        header.AddThemeFontOverride("font", ExtractionTheme.Bold);
        header.AddThemeFontSizeOverride("font_size", 27);
        header.AddThemeColorOverride("font_color", ExtractionTheme.PrimaryHover);
        body.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _permanentChallengeList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _permanentChallengeList.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_permanentChallengeList);
        body.AddChild(scroll);
        return column;
    }

    /// <summary>Rebuilds the challenge list from the store + the session draft. 从存档与会话草稿重建挑战列表。</summary>
    private void RefreshChallengePage()
    {
        ChallengeStore.EnsureDailyRolled();
        NormalizePendingChallengeDraft();
        ClearChildren(_dailyChallengeList);
        ClearChildren(_permanentChallengeList);

        ChallengeData data = ChallengeStore.Current;
        foreach (string id in data.DailyIds)
        {
            ChallengeDef? def = ChallengeRegistry.Get(id);
            if (def != null && MatchesChallengeFilter(def))
            {
                _dailyChallengeList.AddChild(BuildChallengeEntry(def,
                    clearCount: ChallengeStore.GetClearCount(def.Id),
                    ChallengeEntryLayout.Daily));
            }
        }

        foreach (ChallengeDef def in ChallengeRegistry.Permanents)
        {
            if (!MatchesChallengeFilter(def))
            {
                continue;
            }
            _permanentChallengeList.AddChild(BuildChallengeEntry(def,
                clearCount: ChallengeStore.GetClearCount(def.Id),
                ChallengeEntryLayout.Permanent));
        }

        _challengeHintLabel.Text = _pendingChallenges.Count == 0
            ? ExtractionLocalization.ChallengeNoneHintText()
            : ExtractionLocalization.ChallengeSelectedHintText(_pendingChallenges.Count);
    }

    private bool MatchesChallengeFilter(ChallengeDef definition)
    {
        if (_challengeTagFilter.Selected > 0
            && !definition.Tags.Contains((ChallengeTag)(_challengeTagFilter.Selected - 1)))
        {
            return false;
        }

        string query = _challengeSearchEdit.Text.Trim();
        return query.Length == 0
            || ExtractionLocalization.ChallengeTitle(definition.Id).Contains(query, StringComparison.OrdinalIgnoreCase)
            || ExtractionLocalization.ChallengeDesc(definition.Id).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the STRIKE_ONLY challenge cannot be taken: the warehouse has no carryable 打击-tag card, so the run
    /// would be unplayable (grill-locked: the challenge is disabled at selection rather than falling back to the starter
    /// deck). 打击牌挑战是否不可选：仓库无任何可携带的打击标签卡，选了开局即死（grill 锁定：选择阶段禁用，而非空携带兜底）。
    /// </summary>
    private static bool IsStrikeOnlyUnavailable()
    {
        foreach (ExtractionItemTiles.CardGroup g in WarehouseCache.Cards)
        {
                if (g.Rep.Id is { } id
                    && ModelDb.GetByIdOrNull<CardModel>(id) is { } m
                    && m.Tags.Contains(CardTag.Strike))
            {
                return false;
            }
        }

        return true;
    }

    private Control BuildChallengeEntry(ChallengeDef def, int clearCount, ChallengeEntryLayout layout)
    {
        bool selected = _pendingChallenges.Contains(def.Id);
        // Daily challenges are re-selectable — no completion gate (grill-locked). The only disabled state is the
        // STRIKE_ONLY un-carryable gate, and the permanent list keeps its cleared ✓.
        // 每日挑战可重复选择——无完成闸门（grill 锁定）。禁用态仅剩打击牌不可选门槛；常驻列表保留通关 ✓。
        bool disabled = ChallengeRuntime.FromDefinition(def).HasCarryTag(CardTag.Strike) && IsStrikeOnlyUnavailable();
        string state = clearCount > 0 ? ExtractionLocalization.ChallengeClearCountText(clearCount) : "";

        var entry = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, (layout == ChallengeEntryLayout.Daily ? 130f : 112f)
                + Math.Max(0, def.Rewards.Count - 1) * 28f),
        };
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", ExtractionTheme.ChallengeEntryBox(selected, disabled));
        entry.AddChild(panel);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        margin.AddChild(box);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var toggle = new CheckBox
        {
            ButtonPressed = selected,
            Disabled = disabled,
            CustomMinimumSize = new Vector2(32f, 32f),
        };
        toggle.Toggled += _ => ToggleChallenge(def.Id);
        row.AddChild(toggle);

        // The whole entry is a hit target; the checkbox keeps ownership of clicks on its own small hitbox.
        // 整张条目卡片均可点击；复选框自身的小命中区仍由复选框处理。
        panel.GuiInput += inputEvent =>
        {
            if (disabled
                || inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click
                || toggle.GetGlobalRect().HasPoint(click.Position))
            {
                return;
            }

            ToggleChallenge(def.Id);
            panel.AcceptEvent();
        };

        // Title and description share one font AND one size (grill-locked: 标题和描述字体大小改为一样); the title keeps
        // its blue color for hierarchy, the description stays the neutral text color. Layout: title with colon on
        // the first line, description below aligned under the title start (checkbox indent). 标题与描述统一字体字号
        // （grill 锁定）；标题保留蓝色，描述中性色。布局：标题：在第一行，描述另起一行对齐标题起点（复选框缩进）。
        int textSize = layout == ChallengeEntryLayout.Daily ? 19 : 17;

        // Nested VBox for title+description with the same left indent as the title in the row.
        // 嵌套 VBox 容纳标题行+描述行，与 row 中标题的左缩进对齐。
        var textBox = new VBoxContainer();
        textBox.AddThemeConstantOverride("separation", 4);
        textBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        textBox.MouseFilter = Control.MouseFilterEnum.Ignore;

        // First line: title with colon. The clear-count badge is an independent top-right overlay, so its position
        // stays stable regardless of title length. 第一行：标题：。通关次数徽章独立覆盖在右上角，不受标题长度影响。
        var titleRow = new HBoxContainer();

        var title = MakeLabel($"{ExtractionLocalization.ChallengeTitle(def.Id)}：");
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.AddThemeFontOverride("font", ExtractionTheme.Regular);
        title.AddThemeFontSizeOverride("font_size", textSize);
        title.AddThemeColorOverride("font_color", ExtractionTheme.PrimaryHover);
        titleRow.AddChild(title);

        textBox.AddChild(titleRow);

        // Second line (if wraps): description aligned under title start. 第二行（如换行）：描述对齐标题起点。
        var desc = MakeLabel(ExtractionLocalization.ChallengeDesc(def.Id));
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        desc.MouseFilter = Control.MouseFilterEnum.Ignore;
        desc.AddThemeFontOverride("font", ExtractionTheme.Regular);
        desc.AddThemeFontSizeOverride("font_size", textSize);
        textBox.AddChild(desc);

        row.AddChild(textBox);
        box.AddChild(row);

        string reward = ChallengeRewardText(def);
        if (reward.Length > 0)
        {
            var rewardRow = new HBoxContainer();
            rewardRow.AddChild(new Control { CustomMinimumSize = new Vector2(32f, 0f) });

            var rewardLabel = MakeLabel(reward);
            rewardLabel.AddThemeColorOverride("font_color", ExtractionTheme.GoldChipText);
            rewardLabel.AddThemeFontSizeOverride("font_size", layout == ChallengeEntryLayout.Daily ? 18 : 16);
            rewardLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            rewardLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            rewardLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            rewardRow.AddChild(rewardLabel);
            box.AddChild(rewardRow);
        }

        if (state.Length > 0)
        {
            var stateLabel = MakeLabel(state);
            stateLabel.HorizontalAlignment = HorizontalAlignment.Right;
            stateLabel.VerticalAlignment = VerticalAlignment.Center;
            stateLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            stateLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            stateLabel.OffsetLeft = -120f;
            stateLabel.OffsetTop = 8f;
            stateLabel.OffsetRight = -10f;
            stateLabel.OffsetBottom = 34f;
            stateLabel.AddThemeColorOverride("font_color", ExtractionTheme.GoldChipText);
            stateLabel.AddThemeFontSizeOverride("font_size", ExtractionTheme.FontSizeSmall);
            entry.AddChild(stateLabel);
        }

        return entry;
    }

    /// <summary>Toggle a challenge in the session draft; live-clamps the carry + refreshes the warehouse tiles.
    /// 切换会话草稿中的挑战；实时钳制携带并刷新仓库瓦片。</summary>
    private void ToggleChallenge(string id)
    {
        // The STRIKE_ONLY gate is enforced at the UI (disabled tile) and here as defense-in-depth: a challenge that
        // cannot be taken must never enter the draft. 打击牌门槛在 UI（禁用瓦片）与本处双重把关：不可选的挑战绝不进草稿。
        if (!ChallengeRegistry.TryResolveId(id, out string resolvedId))
        {
            return;
        }

        id = resolvedId;
        if (!_pendingChallenges.Contains(id)
            && ChallengeRegistry.Get(id) is { } def
            && ChallengeRuntime.FromDefinition(def).HasCarryTag(CardTag.Strike)
            && IsStrikeOnlyUnavailable())
        {
            return;
        }

        if (_pendingChallenges.Contains(id))
        {
            _pendingChallenges.Remove(id);
        }
        else
        {
            ChallengeSelectionResult selection = ChallengeSelectionService.NormalizeHubDraft(
                _pendingChallenges.Append(id), ChallengeStore.Current.DailyIds);
            if (selection.IsRejected(id))
            {
                Entry.Logger.Warn($"WarehouseHub: rejected challenge selection '{id}'.");
                return;
            }

            _pendingChallenges.Clear();
            _pendingChallenges.AddRange(selection.Ids);
        }

        ClampDraftToChallenges();
        RefreshChallengePage();
        Refresh();
    }

    /// <summary>Drops any still-selected daily that fell out of the refreshed pool (console refresh). 移除被刷新池
    /// 换出的已选每日——开跑不会带一个不在池中的 id。</summary>
    public void RemovePendingChallengesNotInDailyPool()
    {
        int previousCount = _pendingChallenges.Count;
        NormalizePendingChallengeDraft();
        if (_pendingChallenges.Count != previousCount)
        {
            ClampDraftToChallenges();
        }

        RefreshChallengePage();
    }

    private void NormalizePendingChallengeDraft()
    {
        ChallengeSelectionResult selection = ChallengeSelectionService.NormalizeHubDraft(
            _pendingChallenges, ChallengeStore.Current.DailyIds);
        if (_pendingChallenges.SequenceEqual(selection.Ids))
        {
            return;
        }

        if (selection.RejectedIds.Count > 0)
        {
            Entry.Logger.Warn("WarehouseHub: removed invalid/stale challenge draft entries: " +
                              string.Join(", ", selection.RejectedIds));
        }
        _pendingChallenges.Clear();
        _pendingChallenges.AddRange(selection.Ids);
    }

    private static string ChallengeRewardText(ChallengeDef def)
    {
        return string.Join("\n", def.Rewards.Select(ChallengeRewardActionText));
    }

    private static string ChallengeRewardActionText(ChallengeRewardAction action)
    {
        if (action is DoubleReturnedCarryRewardAction)
        {
            return ExtractionLocalization.ChallengeRewardDoubleText();
        }

        if (action is GrantFixedCardsRewardAction fixedCards)
        {
            CardModel? fixedCard = ModelDb.GetByIdOrNull<CardModel>(new ModelId(ModelId.SlugifyCategory<CardModel>(), fixedCards.CardIds[0]));
            string name = fixedCard?.Title ?? fixedCards.CardIds[0];
            return ExtractionLocalization.ChallengeRewardFixedText(fixedCards.Count, name);
        }

        if (action is GrantAllCharacterCardsRewardAction)
        {
            return ExtractionLocalization.ChallengeRewardAllCardsText();
        }

        if (action is GrantRelicRarityRewardAction relicsByRarity)
        {
            // ONE_REST grants Ancient relics — hardcode the native 「先古」 term (matches the game's relic_collection
            // naming; the generic rarity label would say 远古). 先古遗物专用文案——对齐游戏「先古」译名。
            return relicsByRarity.Rarity == RelicRarity.Ancient
                ? ExtractionLocalization.ChallengeRewardAncientRelicsText(relicsByRarity.Count)
                : ExtractionLocalization.ChallengeRewardRandomRelicText(relicsByRarity.Count,
                    ExtractionLocalization.FilterRarityLabel(relicsByRarity.Rarity.ToString().ToLowerInvariant()));
        }

        if (action is GrantFixedRelicsRewardAction fixedRelics)
        {
            return ExtractionLocalization.ChallengeRewardFixedRelicsText(fixedRelics.Count);
        }

        if (action is GrantCardRarityRewardAction cardsByRarity)
        {
            string rarityName = ExtractionLocalization.FilterRarityLabel(cardsByRarity.Rarity.ToString().ToLowerInvariant());
            return cardsByRarity.Count > 0
                ? ExtractionLocalization.ChallengeRewardRandomText(cardsByRarity.Count, rarityName)
                : ExtractionLocalization.ChallengeRewardAllText(rarityName);
        }

        if (action is GrantGoldRewardAction gold)
        {
            return ExtractionLocalization.ChallengeRewardGoldText(gold.Amount);
        }

        throw new InvalidOperationException($"Unknown challenge reward action: {action.GetType().Name}");
    }
}
