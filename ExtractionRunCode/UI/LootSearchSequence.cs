using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.RuntimeInput;
using ExtractionRun.Networking;
using ExtractionRun.Settings;

namespace ExtractionRun.UI;

/// <summary>One item in a loot-search sequence: the node to hide/reveal, its search duration, and optionally a
/// precomputed **final** global rect that bounds the search overlay plus a model node to hide directly. The rect is
/// supplied when the hide target's own rect doesn't match the visual — e.g. the 0-sized card holder whose card renders
/// via a centered Hitbox child, or a merchant slot whose card/relic/potion icon sits offset from the slot rect. The
/// patches compute the FINAL rect up front (the entrance animations are pure known translations), so every gray cover
/// rect sits on the settled slot while the entrance plays.
/// 搜刮序列中的一个物品：要隐藏/揭示的节点、搜刮时长，以及可选的预计算**最终**全局矩形（覆盖搜刮遮罩）+ 直接隐藏的模型节点。
/// 隐藏目标自身矩形与视觉不符时需提供矩形——如 0 尺寸的卡牌容器根节点（卡牌由居中的 Hitbox 子节点渲染）、
/// 或模型图标相对槽位矩形有偏移的商人槽位。补丁在开场时算好最终矩形（入场动画均为已知纯位移），让所有灰格在入场播放时就位。</summary>
public readonly record struct LootSearchEntry(Control Target, float DurationSeconds, Rect2? RectSource = null, Control? HideTarget = null);

/// <summary>
/// The 搜刮 loot-search reveal: every item is covered by a same-size dark-gray rect from the moment the screen opens,
/// then all items search **simultaneously** — each gets its own <c>cursor_inspect</c> icon orbiting its center clockwise
/// and hard-cuts to show it when its own duration elapses. Clicking a still-searching item reveals just that item early;
/// the skip key (a RitsuLib hotkey binding, default Space) reveals everything remaining at once.
/// The <see cref="LootSearch"/> facade applies the extraction-only gate.
/// 搜刮揭示动画：开场即用等大深灰矩形盖住每个物品，然后所有物品**同时**开搜——各自用 cursor_inspect 图标绕中心顺时针公转，
/// 到时硬切揭示。点击仍在搜刮的物品只提前揭示该件；跳过键（RitsuLib 热键绑定，默认空格）一次揭示全部剩余。
/// LootSearch 门面负责「仅搜打撤局」判定。
/// </summary>
public static class LootSearch
{
    internal const string MagnifierPath = "res://images/packed/common_ui/cursor_inspect.png";

    /// <summary>
    /// Whether the loot-search should run right now: the setting is ON and the current run carries the extraction
    /// modifier. Never throws — a settings/state hiccup must not break a vanilla reward screen.
    /// 搜刮动画此刻是否应运行：设置开启且当前局带搜打撤 modifier。绝不抛异常——设置/状态异常不能破坏原版奖励界面。
    /// </summary>
    public static bool ShouldRun()
    {
        try
        {
            if (!ExtractionSettingsPage.Current.LootAnimationEnabled)
            {
                return false;
            }

            return ExtractionCarrySync.HasExtractionModifier(RunManager.Instance?.State?.Modifiers ?? []);
        }
        catch (Exception e)
        {
            Entry.Logger.Warn($"LootSearch.ShouldRun failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Duration (seconds) for a card's rarity. 卡牌稀有度对应的搜刮时长（秒）。</summary>
    public static float DurationFor(CardRarity rarity) => DurationFor(rarity, Settings);

    private static float DurationFor(CardRarity rarity, ExtractionSettings settings) => rarity switch
    {
        CardRarity.Basic or CardRarity.Common => settings.LootAnimationBasicCommonDuration,
        CardRarity.Uncommon => settings.LootAnimationUncommonDuration,
        CardRarity.Rare => settings.LootAnimationRareDuration,
        CardRarity.Ancient => settings.LootAnimationAncientDuration,
        _ => settings.LootAnimationOtherDuration,
    };

    /// <summary>Duration (seconds) for a relic's rarity. 遗物稀有度对应的搜刮时长（秒）。</summary>
    public static float DurationFor(RelicRarity rarity) => DurationFor(rarity, Settings);

    private static float DurationFor(RelicRarity rarity, ExtractionSettings settings) => rarity switch
    {
        RelicRarity.Starter or RelicRarity.Common => settings.LootAnimationBasicCommonDuration,
        RelicRarity.Uncommon => settings.LootAnimationUncommonDuration,
        RelicRarity.Rare => settings.LootAnimationRareDuration,
        RelicRarity.Ancient => settings.LootAnimationAncientDuration,
        _ => settings.LootAnimationOtherDuration,
    };

    private static ExtractionSettings Settings => ExtractionSettingsPage.Current;

    /// <summary>
    /// Starts a loot-search sequence over <paramref name="parent"/>. Every item's gray cover rect is spawned immediately
    /// at its (precomputed final) rect; after <paramref name="entranceSeconds"/> all items search at once — each gets its
    /// own orbiting magnifier and hard-cuts revealed when its own duration elapses. <paramref name="stillActive"/> is
    /// checked once after the entrance so a sequence can't run over a closed/hidden screen (e.g. the merchant, which stays
    /// in the tree after Close). Returns the overlay node.
    /// 在 <paramref name="parent"/> 上启动搜刮序列：立即按（预计算的最终）矩形为每个物品铺灰格；等 entranceSeconds 入场播完后
    /// 所有物品同时开搜——各自生成绕行放大镜，按各自时长到时硬切揭示。stillActive 在入场结束后检查一次，防止序列盖在已关闭/隐藏的
    /// 界面（如商人，Close 后仍在树中）上。返回覆盖层节点。
    /// </summary>
    public static LootSearchSequence? Play(Control parent, IEnumerable<LootSearchEntry> entries, float entranceSeconds, Func<bool>? stillActive = null)
    {
        var list = entries.Where(e => e.Target != null && GodotObject.IsInstanceValid(e.Target)).ToList();
        if (list.Count == 0 || parent == null || !GodotObject.IsInstanceValid(parent))
        {
            return null;
        }

        var sequence = new LootSearchSequence(list, entranceSeconds, stillActive);
        parent.AddChild(sequence);
        return sequence;
    }
}

/// <summary>
/// The full-screen overlay that renders the loot-search. Added as the last child of a reward screen so it sits on top
/// and swallows clicks for the sequence's duration (MouseFilter.Stop); freed on completion. The sequence is driven by
/// <see cref="LootSearch.Play"/> — never construct directly.
/// 渲染搜刮的全屏覆盖层。作为奖励屏最后一个子节点加入（盖在最上，序列期间吞掉点击）；结束后释放。由 LootSearch.Play 驱动。
/// </summary>
public sealed partial class LootSearchSequence : Control
{
    private static readonly Color GrayColor = new(0.11f, 0.11f, 0.12f, 1f);

    /// <summary>Reveal cue: the vanilla debug-audio card-deal clip (res://debug_audio/card_deal.mp3 — the asset the
    /// base game itself ships and plays for card deals), not an FMOD event. Volume is boosted above 1f and the pitch
    /// nudged per play so repeated reveals read clearly instead of sounding identical.
    /// 搜刮揭示音：原版 card_deal.mp3（base 游戏自带的发牌音效，非 FMOD 事件）。音量调高并加轻微随机音高。</summary>
    private const float RevealSfxVolume = 1.5f;

    private readonly List<LootSearchEntry> _items;
    private readonly float _entranceSeconds;
    private readonly Func<bool>? _stillActive;
    private readonly List<ColorRect> _rects = new();
    private readonly List<ItemState> _states = new();
    private readonly TaskCompletionSource<SkipReason> _skipAll = new();

    private IRuntimeHotkeyHandle? _skipHandle;
    private static ImageTexture? _magnifierTexture;

    private enum SkipReason
    {
        Completed,
        Advance,
        SkipAll,
        Cancelled,
    }

    /// <summary>Per-item search state: its orbiting magnifier (position driven each frame) plus its click-advance source.
    /// 每件物品的搜刮状态：绕行放大镜（逐帧驱动位置）+ 点击提前揭示源。</summary>
    private sealed class ItemState
    {
        public TextureRect? Magnifier;
        public Vector2 OrbitCenter;
        public float OrbitRadius;
        public float OrbitDuration;
        public float OrbitElapsed;
        public TaskCompletionSource<SkipReason>? Advance;
    }

    internal LootSearchSequence(List<LootSearchEntry> items, float entranceSeconds, Func<bool>? stillActive)
    {
        _items = items;
        _entranceSeconds = entranceSeconds;
        _stillActive = stillActive;
        Name = "LootSearchSequence";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _states = items.Select(_ => new ItemState()).ToList();
        RegisterSkipHotkey();
    }

    public override void _Ready()
    {
        // Cover every item with its gray rect right away — the entries carry the FINAL rects (the patches derive them
        // from each screen's known entrance translation), so the boxes sit on the settled slots while the entrance
        // animation plays unseen; the search then reveals all of them at once, each as its own duration elapses.
        // 开场即用灰格盖住每个物品——条目携带最终矩形（补丁按各界面已知入场位移算好），箱子在入场动画播放时就位；
        // 随后所有物品同时开搜，各按自身时长揭示。
        foreach (LootSearchEntry item in _items)
        {
            ColorRect gray = CreateGrayRect(ItemRect(item));
            AddChild(gray);
            _rects.Add(gray);
        }

        TaskHelper.RunSafely(PlayCoveredAsync());
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
        {
            // A click inside any still-searching item reveals just that item — mouse players click through one by one.
            // 点击仍在搜刮的物品只提前揭示该件——鼠标玩家可以逐件点掉。
            for (int i = 0; i < _items.Count; i++)
            {
                ItemState state = _states[i];
                if (state.Advance == null || !GodotObject.IsInstanceValid(_items[i].Target))
                {
                    continue;
                }
                if (ItemRect(_items[i]).HasPoint(mouse.Position))
                {
                    state.Advance.TrySetResult(SkipReason.Advance);
                    break;
                }
            }
        }
    }

    public override void _ExitTree()
    {
        _skipAll.TrySetResult(SkipReason.Cancelled);
        foreach (ItemState state in _states)
        {
            state.Advance?.TrySetResult(SkipReason.Cancelled);
            state.Advance = null;
        }
        _skipHandle?.Dispose();
        _skipHandle = null;
    }

    private async Task PlayCoveredAsync()
    {
        await Cmd.Wait(_entranceSeconds, ignoreCombatEnd: true);
        if (!IsInsideTree())
        {
            return;
        }

        if (_stillActive != null && !_stillActive())
        {
            RevealAll();
            Finish();
            return;
        }

        // All items search at the same time — each spawns its own orbiting magnifier and reveals when its own duration
        // elapses (click-advance reveals just that item; the skip key reveals everything remaining).
        // 所有物品同时开搜——各自生成绕行放大镜，按各自时长揭示（点击提前揭示单件；跳过键一次揭示全部剩余）。
        var tasks = new List<Task<bool>>(_items.Count);
        for (int i = 0; i < _items.Count; i++)
        {
            SpawnMagnifier(i);
            tasks.Add(RevealItemAsync(i));
        }

        // The reveal cue is per-item inside RevealItemAsync — nothing plays at sequence end.
        await Task.WhenAll(tasks);
        Finish();
    }

    /// <summary>Reveals one item after its own duration (or a click on it / the skip key): hard-cut — free that item's
    /// (already-present) cover rect and show it. Returns true when the whole sequence was skip-all'd.
    /// 揭示一件物品：按自身时长（或点击它/跳过键）硬切——移除该格并显示物品。返回 true 表示整段被 skip-all。</summary>
    private async Task<bool> RevealItemAsync(int index)
    {
        LootSearchEntry item = _items[index];
        ItemState state = _states[index];

        state.Advance = new TaskCompletionSource<SkipReason>();
        Task delay = Cmd.Wait(item.DurationSeconds, ignoreCombatEnd: true);
        Task winner = await Task.WhenAny(delay, state.Advance.Task, _skipAll.Task);
        if (!IsInsideTree())
        {
            return true; // overlay freed mid-item — nothing more to reveal
        }

        SkipReason reason = winner == state.Advance.Task ? state.Advance.Task.Result
            : winner == _skipAll.Task ? _skipAll.Task.Result
            : SkipReason.Completed;
        state.Advance = null;

        ClearMagnifier(index);
        FreeCover(index);
        item.Target.Visible = true;
        if (item.HideTarget != null && GodotObject.IsInstanceValid(item.HideTarget))
        {
            item.HideTarget.Visible = true;
        }

        // 每件物品揭示（硬切）的一刻播放发牌音；点击提前揭示同样触发。skip-all 整段静默揭示——一堆发牌声同时响会糊成一团。
        // The manager node lives under NGame, so the clip outlives this overlay when it frees right after.
        if (reason != SkipReason.SkipAll && (_stillActive == null || _stillActive()))
        {
            NDebugAudioManager.Instance?.Play(TmpSfx.cardDeal, RevealSfxVolume, PitchVariance.Small);
        }
        return reason == SkipReason.SkipAll;
    }

    private void SpawnMagnifier(int index)
    {
        Rect2 rect = ItemRect(_items[index]);
        ItemState state = _states[index];
        float minDim = Mathf.Min(rect.Size.X, rect.Size.Y);
        float magSize = Mathf.Clamp(minDim * 0.5f, 32f, 96f);
        float radius = Mathf.Clamp(minDim * 0.35f, 24f, 120f);

        state.Magnifier = new TextureRect { Texture = MagnifierTexture(), Size = new Vector2(magSize, magSize) };
        state.Magnifier.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(state.Magnifier);

        // The magnifier never rotates itself — it orbits the item center, so drive its POSITION each frame. Positive
        // angle = clockwise in Godot's y-down space (right → down → left → up).
        state.OrbitCenter = rect.Position + rect.Size * 0.5f;
        state.OrbitRadius = radius;
        state.OrbitDuration = Mathf.Max(_items[index].DurationSeconds, 0.1f);
        state.OrbitElapsed = 0f;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        bool any = false;
        foreach (ItemState state in _states)
        {
            if (state.Magnifier == null)
            {
                continue;
            }
            any = true;
            state.OrbitElapsed += (float)delta;
            float t = Mathf.Min(state.OrbitElapsed / state.OrbitDuration, 1f);
            float angle = t * Mathf.Tau;
            state.Magnifier.Position = state.OrbitCenter
                + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * state.OrbitRadius
                - state.Magnifier.Size * 0.5f;
        }
        SetProcess(any);
    }

    private void ClearMagnifier(int index)
    {
        ItemState state = _states[index];
        if (GodotObject.IsInstanceValid(state.Magnifier))
        {
            state.Magnifier.QueueFree();
        }
        state.Magnifier = null;
        // _Process keeps processing while any magnifier remains; it turns itself off once the last one is cleared.
    }

    private static ColorRect CreateGrayRect(Rect2 rect) =>
        new() { Color = GrayColor, Position = rect.Position, Size = rect.Size, MouseFilter = MouseFilterEnum.Ignore };

    private void FreeCover(int index)
    {
        if (index >= 0 && index < _rects.Count && GodotObject.IsInstanceValid(_rects[index]))
        {
            _rects[index].QueueFree();
        }
    }

    /// <summary>Reveals every item at once, silently (the screen went away right after the entrance — nothing left to
    /// animate). 一次性静默揭示所有物品（界面在入场后随即关闭，无剩余动画）。</summary>
    private void RevealAll()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            FreeCover(i);
            LootSearchEntry item = _items[i];
            if (GodotObject.IsInstanceValid(item.Target))
            {
                item.Target.Visible = true;
            }
            if (item.HideTarget != null && GodotObject.IsInstanceValid(item.HideTarget))
            {
                item.HideTarget.Visible = true;
            }
            ClearMagnifier(i);
        }
    }

    private void Finish()
    {
        _skipHandle?.Dispose();
        _skipHandle = null;
        QueueFree();
    }

    private void RegisterSkipHotkey()
    {
        try
        {
            string binding = RuntimeHotkeyService.NormalizeOrDefault(ExtractionSettingsPage.Current.LootAnimationSkipKey, "Space");
            _skipHandle = RuntimeHotkeyService.Register(binding, OnSkipHotkey,
                new RuntimeHotkeyOptions { DebugName = "ExtractionRun loot search skip" });
        }
        catch (Exception e)
        {
            Entry.Logger.Warn($"LootSearchSequence: skip hotkey unavailable ({e.Message})");
        }
    }

    private void OnSkipHotkey() => _skipAll.TrySetResult(SkipReason.SkipAll);

    private Rect2 ItemRect(LootSearchEntry item)
    {
        // RectSource is a precomputed final global rect (the node whose bounds match the visual); fall back to the hide
        // target itself when none was supplied.
        Rect2 global = item.RectSource ?? item.Target.GetGlobalRect();
        // Same-space subtraction: both rects come from GetGlobalRect(), so the overlay's top-left maps the item's rect
        // into overlay-local coordinates regardless of any canvas/layer offset (the overlay is full-rect, scale 1).
        // Mixing GetGlobalRect with GetGlobalTransform() here previously produced wrong/mirrored positions.
        Vector2 origin = GetGlobalRect().Position;
        return new Rect2(global.Position - origin, global.Size);
    }

    private static ImageTexture MagnifierTexture()
    {
        if (_magnifierTexture != null)
        {
            return _magnifierTexture;
        }

        try
        {
            Image image = PreloadManager.Cache.GetAsset<Image>(LootSearch.MagnifierPath);
            _magnifierTexture = ImageTexture.CreateFromImage(image);
        }
        catch (Exception e)
        {
            Entry.Logger.Warn($"LootSearchSequence: magnifier texture load failed ({e.Message}); using placeholder.");
            _magnifierTexture = ImageTexture.CreateFromImage(CreatePlaceholderMagnifier());
        }

        return _magnifierTexture;
    }

    /// <summary>Defensive zero-asset fallback if <c>cursor_inspect</c> can't be loaded: a plain ring on a transparent
    /// 64×64 image. 防御性零资源回退：cursor_inspect 加载失败时用透明底 + 圆环的 64×64 图。</summary>
    private static Image CreatePlaceholderMagnifier()
    {
        const int size = 64;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt(Mathf.Pow(x - size * 0.5f, 2f) + Mathf.Pow(y - size * 0.5f, 2f));
                if (Mathf.Abs(dist - size * 0.28f) < 4f)
                {
                    image.SetPixel(x, y, Colors.White);
                }
            }
        }

        return image;
    }
}
