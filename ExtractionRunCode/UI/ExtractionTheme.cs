using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace ExtractionRun.UI;

/// <summary>
/// Unified flat "card" theme for the 搜打撤 warehouse hub, built entirely in code. Every surface is drawn
/// with StyleBoxFlat (backgrounds, rounded corners, soft shadows) — no image textures. Fonts are the base
/// game's own: the per-language substitute font when the locale needs one (CJK etc.), Kreon otherwise — the
/// same resolution the game applies via FontControlUtils — so the hub reads as vanilla UI.
/// 搜打撤仓库大厅的统一扁平深色卡片主题（纯代码构建，全用 StyleBoxFlat）；字体取自游戏原版（按语言替换 / Kreon）。
/// </summary>
public static class ExtractionTheme
{
    // ----- Palette 调色板 (dark) -----
    /// <summary>Page background. 页面背景。</summary>
    public static readonly Color Background = new("12161F");

    /// <summary>Card surface. 卡片表面。</summary>
    public static readonly Color Card = new("1B2230");

    /// <summary>Raised surface (hovered tiles). 浮起表面（悬停卡片）。</summary>
    public static readonly Color CardRaised = new("232C3E");

    /// <summary>Accent blue (Material Blue, brightened for dark bg). 主题蓝。</summary>
    public static readonly Color Primary = new("4C9AFF");

    public static readonly Color PrimaryHover = new("67ACFF");
    public static readonly Color PrimaryPressed = new("3B82E0");

    /// <summary>Primary text. 主要文字。</summary>
    public static readonly Color Text = new("E9EDF5");

    /// <summary>Secondary text. 次要文字。</summary>
    public static readonly Color TextSecondary = new("8E9AB0");

    /// <summary>Hairline border / divider. 分割线与描边。</summary>
    public static readonly Color Border = new("2C3546");

    /// <summary>Hover tint for flat list rows. 列表行悬停底色。</summary>
    public static readonly Color RowHover = new("263044");

    /// <summary>Pressed tint for flat list rows. 列表行按下底色。</summary>
    public static readonly Color RowPressed = new("2E3A52");

    /// <summary>Gold pill on the dark theme. 金币胶囊（深色）。</summary>
    public static readonly Color GoldChip = new("3A2F1D");

    public static readonly Color GoldChipBorder = new("C9A54B");
    public static readonly Color GoldChipText = new("F0C75E");
    public static readonly Color ScrollGrabber = new("46526A");
    public static readonly Color ScrollGrabberActive = Primary;
    public static readonly Color Shadow = new(0f, 0f, 0f, 0.35f);

    /// <summary>Item tile quantity badge background. 物品卡片数量角标底色。</summary>
    public static readonly Color Badge = new("10151E");

    public static readonly Color BadgeBorder = new("3A4A66");
    public static readonly Color BadgeText = new("EAF1FF");

    /// <summary>Texture well behind tile art. 物品贴图凹槽底色。</summary>
    public static readonly Color TextureWell = new("0E131B");

    /// <summary>Warning tone (empty-carry start hint). 警示色（空携带提示）。</summary>
    public static readonly Color Danger = new("E5484D");

    /// <summary>Sell-selection count green (glyph chip shows how many copies are selected). 出售选中数绿色（角标显示选中份数）。</summary>
    public static readonly Color SellSelectedGreen = new("4ADE80");

    public const int FontSizeBody = 16;
    public const int FontSizeSmall = 14;
    public const int FontSizeTitle = 26;
    public const int FontSizeSection = 18;

    // ----- Button theme-type variations -----
    public const string ButtonPrimary = "primary";
    public const string ButtonSecondary = "secondary";
    public const string ButtonRow = "row";
    public const string ButtonTile = "tile";
    public const string ButtonTab = "tab";
    public const string ButtonSegment = "segment";

    private static Theme? _theme;
    private static Font? _regular;
    private static Font? _bold;

    /// <summary>The cached shared theme. 缓存的共享主题。</summary>
    public static Theme Instance => _theme ??= Build();

    /// <summary>Vanilla regular font for the current locale. 当前语言的原版常规字体。</summary>
    public static Font? Regular => _regular ??= ResolveFont(FontType.Regular);

    /// <summary>Vanilla bold font for the current locale. 当前语言的原版加粗字体。</summary>
    public static Font? Bold => _bold ??= ResolveFont(FontType.Bold);

    // ----- Shared surfaces 共享表面样式 -----

    /// <summary>Full-rect hub background, 90% opaque so the menu behind stays faintly visible. 大厅整屏背景（90% 不透明度）。</summary>
    public static StyleBoxFlat BackgroundBox()
    {
        StyleBoxFlat sb = Box(Background);
        sb.BgColor = new Color(Background.R, Background.G, Background.B, 0.9f);
        return sb;
    }

    /// <summary>Floating dark card with a hairline border and a soft shadow. 悬浮深色卡片。</summary>
    public static StyleBoxFlat CardBox() => Box(Card, radius: 12, border: Border, borderWidth: 1, shadowSize: 10);

    /// <summary>Unified frame for the header page switcher. The child buttons supply the three equal segments.
    /// 顶部页面切换器的统一外框，内部按钮负责三个等宽分段。</summary>
    public static StyleBoxFlat PageSwitcherBox() => Box(Card, radius: 8, border: Border, borderWidth: 1);

    /// <summary>Pill-shaped gold chip (dark gold on the dark theme). 金币胶囊（深色主题）。</summary>
    public static StyleBoxFlat ChipBox()
    {
        StyleBoxFlat sb = Box(GoldChip, radius: 999, border: GoldChipBorder, borderWidth: 1);
        sb.ContentMarginLeft = 14;
        sb.ContentMarginRight = 14;
        sb.ContentMarginTop = 6;
        sb.ContentMarginBottom = 6;
        return sb;
    }

    /// <summary>Transparent scrollbar track / scroll groove. 透明滚动条轨道。</summary>
    public static StyleBoxFlat ScrollTrackBox() => Box(new Color(0f, 0f, 0f, 0f));

    private static StyleBoxFlat ScrollGrabberBox()
    {
        StyleBoxFlat sb = Box(ScrollGrabber, radius: 4);
        sb.ContentMarginLeft = 3;
        sb.ContentMarginRight = 3;
        return sb;
    }

    private static StyleBoxFlat ScrollGrabberActiveBox()
    {
        StyleBoxFlat sb = Box(ScrollGrabberActive, radius: 4);
        sb.ContentMarginLeft = 3;
        sb.ContentMarginRight = 3;
        return sb;
    }

    private static StyleBoxFlat PrimaryButtonBox()
    {
        StyleBoxFlat sb = Box(Primary, radius: 10, shadowSize: 5);
        sb.SetContentMarginAll(14);
        sb.ContentMarginTop = 11;
        sb.ContentMarginBottom = 11;
        return sb;
    }

    private static StyleBoxFlat PrimaryButtonHoverBox()
    {
        StyleBoxFlat sb = Box(PrimaryHover, radius: 10, shadowSize: 6);
        sb.SetContentMarginAll(14);
        sb.ContentMarginTop = 11;
        sb.ContentMarginBottom = 11;
        return sb;
    }

    private static StyleBoxFlat PrimaryButtonPressedBox()
    {
        StyleBoxFlat sb = Box(PrimaryPressed, radius: 10, shadowSize: 3);
        sb.SetContentMarginAll(14);
        sb.ContentMarginTop = 11;
        sb.ContentMarginBottom = 11;
        return sb;
    }

    private static StyleBoxFlat SecondaryButtonBox(bool hovered)
    {
        StyleBoxFlat sb = Box(hovered ? RowHover : Card, radius: 10, border: Border, borderWidth: 1,
            shadowSize: hovered ? 5 : 3);
        sb.SetContentMarginAll(14);
        sb.ContentMarginTop = 10;
        sb.ContentMarginBottom = 10;
        return sb;
    }

    private static StyleBoxFlat SecondaryButtonPressedBox()
    {
        StyleBoxFlat sb = Box(new Color("1E2634"), radius: 10, border: Border, borderWidth: 1, shadowSize: 2);
        sb.SetContentMarginAll(14);
        sb.ContentMarginTop = 10;
        sb.ContentMarginBottom = 10;
        return sb;
    }

    private static StyleBoxFlat RowButtonBox(bool hovered)
    {
        StyleBoxFlat sb = Box(hovered ? RowHover : new Color(0f, 0f, 0f, 0f), radius: 8);
        sb.SetContentMarginAll(10);
        sb.ContentMarginTop = 7;
        sb.ContentMarginBottom = 7;
        return sb;
    }

    private static StyleBoxFlat RowButtonPressedBox()
    {
        StyleBoxFlat sb = Box(RowPressed, radius: 8);
        sb.SetContentMarginAll(10);
        sb.ContentMarginTop = 7;
        sb.ContentMarginBottom = 7;
        return sb;
    }

    /// <summary>Tab bar button (unselected). 未选中的 Tab 按钮。</summary>
    private static StyleBoxFlat TabButtonBox(bool hovered)
    {
        return Box(hovered ? RowHover : Card, radius: 8, border: Border, borderWidth: 1);
    }

    /// <summary>Tab bar button (selected — primary accent border). 选中的 Tab 按钮（主题蓝描边）。</summary>
    private static StyleBoxFlat TabButtonPressedBox()
    {
        return Box(CardRaised, radius: 8, border: Primary, borderWidth: 2);
    }

    /// <summary>Flat segment inside the shared page-switcher frame. 顶部共用长条中的扁平分段。</summary>
    private static StyleBoxFlat SegmentButtonBox(bool hovered)
    {
        return Box(hovered ? RowHover : new Color(0f, 0f, 0f, 0f));
    }

    private static StyleBoxFlat SegmentButtonPressedBox() => Box(Primary);

    // ----- Item tiles (warehouse / carry card-form entries) -----

    /// <summary>Item tile surface: dark rounded card with a hairline border. 物品卡片表面。</summary>
    private static StyleBoxFlat TileBox(bool hovered)
    {
        StyleBoxFlat sb = Box(hovered ? CardRaised : Card, radius: 10,
            border: hovered ? Primary : Border, borderWidth: 1, shadowSize: hovered ? 5 : 2);
        return sb;
    }

    private static StyleBoxFlat TileBoxPressed()
    {
        StyleBoxFlat sb = Box(new Color("18202E"), radius: 10, border: Border, borderWidth: 1, shadowSize: 1);
        return sb;
    }

    /// <summary>Recessed well behind the tile's art. 物品贴图凹槽。</summary>
    public static StyleBoxFlat TextureWellBox() => Box(TextureWell, radius: 6);

    /// <summary>Quantity pill (semi-transparent dark). 数量角标。</summary>
    public static StyleBoxFlat BadgeBox()
    {
        StyleBoxFlat sb = Box(Badge, radius: 999, border: BadgeBorder, borderWidth: 1);
        sb.ContentMarginLeft = 7;
        sb.ContentMarginRight = 7;
        sb.ContentMarginTop = 2;
        sb.ContentMarginBottom = 2;
        return sb;
    }

    /// <summary>Circular add / remove chip. 添加 / 移除圆形角标。</summary>
    public static StyleBoxFlat GlyphBox(bool add)
    {
        return Box(add ? Primary : new Color("4A5568"), radius: 999);
    }

    /// <summary>Circular selected-count chip (green — the sell multi-select shows the count, not a check).
    /// 选中数圆形角标（绿——出售多选显示份数而非对勾）。</summary>
    public static StyleBoxFlat SelectedGlyphBox() => Box(SellSelectedGreen, radius: 999);

    /// <summary>Compact gold pill for the shop tile price. 商店瓦片的价格胶囊。</summary>
    public static StyleBoxFlat PriceBox()
    {
        StyleBoxFlat sb = Box(GoldChip, radius: 999, border: GoldChipBorder, borderWidth: 1);
        sb.ContentMarginLeft = 8;
        sb.ContentMarginRight = 8;
        sb.ContentMarginTop = 2;
        sb.ContentMarginBottom = 2;
        return sb;
    }

    /// <summary>Selected (multi-select) item tile surface: primary border on the raised card. 选中瓦片表面（主题蓝描边）。</summary>
    public static StyleBoxFlat SelectedTileBox() => Box(CardRaised, radius: 10, border: Primary, borderWidth: 2, shadowSize: 3);

    /// <summary>Challenge entry surface: raised card; a selected entry shows the primary border; a disabled entry
    /// (STRIKE_ONLY un-carryable) dims into a muted card. 挑战条目表面：选中加主题蓝描边；禁用（打击牌不可带）降为暗色卡片。</summary>
    public static StyleBoxFlat ChallengeEntryBox(bool selected, bool disabled = false)
    {
        if (disabled)
        {
            return Box(CardRaised, radius: 10, border: Border, borderWidth: 1, shadowSize: 2);
        }

        return Box(selected ? CardRaised : Card, radius: 10,
            border: selected ? Primary : Border, borderWidth: 1, shadowSize: 2);
    }

    /// <summary>Search input surface: dark card, primary border + slightly raised fill while focused. 搜索输入框表面。</summary>
    private static StyleBoxFlat LineEditBox(bool focused)
    {
        StyleBoxFlat sb = Box(focused ? new Color("20293B") : Card, radius: 8,
            border: focused ? Primary : Border, borderWidth: 1);
        sb.SetContentMarginAll(10);
        return sb;
    }

    private static StyleBoxFlat FocusBox() => Box(new Color(0f, 0f, 0f, 0f), radius: 9, border: Primary, borderWidth: 2);

    // ----- Theme assembly -----

    /// <summary>
    /// Builds the unified theme once. Applied to the hub root so every control (cards, buttons, labels,
    /// scrollbars, separators) inherits the same flat palette and vanilla fonts.
    /// 构建统一主题并设置到大厅根节点，令所有控件共享扁平配色与原版字体。
    /// </summary>
    public static Theme Build()
    {
        var theme = new Theme
        {
            DefaultFont = Regular,
            DefaultFontSize = FontSizeBody,
        };

        // ----- Label -----
        theme.SetColor("font_color", "Label", Text);
        theme.SetFontSize("font_size", "Label", FontSizeBody);

        // ----- Button (base) -----
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Text);
        theme.SetColor("font_pressed_color", "Button", Text);
        theme.SetColor("font_focus_color", "Button", Text);
        theme.SetColor("font_disabled_color", "Button", TextSecondary);
        theme.SetFontSize("font_size", "Button", FontSizeBody);
        theme.SetConstant("h_separation", "Button", 8);
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());

        // ----- Panel / PanelContainer: dark card surface -----
        theme.SetStylebox("panel", "Panel", CardBox());
        theme.SetStylebox("panel", "PanelContainer", CardBox());

        // ----- ScrollContainer: no own background; lists sit on the card -----
        theme.SetStylebox("panel", "ScrollContainer", Box(new Color(0f, 0f, 0f, 0f)));

        // ----- Button variations -----
        RegisterButtonVariation(theme, ButtonPrimary, Colors.White,
            PrimaryButtonBox(), PrimaryButtonHoverBox(), PrimaryButtonPressedBox(), FontSizeBody);
        RegisterButtonVariation(theme, ButtonSecondary, Text,
            SecondaryButtonBox(false), SecondaryButtonBox(true), SecondaryButtonPressedBox(), FontSizeBody);
        RegisterButtonVariation(theme, ButtonRow, Text,
            RowButtonBox(false), RowButtonBox(true), RowButtonPressedBox(), 15);
        RegisterButtonVariation(theme, ButtonTile, Text,
            TileBox(false), TileBox(true), TileBoxPressed(), FontSizeSmall);
        RegisterButtonVariation(theme, ButtonTab, Text,
            TabButtonBox(false), TabButtonBox(true), TabButtonPressedBox(), FontSizeBody);
        RegisterButtonVariation(theme, ButtonSegment, Text,
            SegmentButtonBox(false), SegmentButtonBox(true), SegmentButtonPressedBox(), FontSizeBody);

        // ----- Thin flat scrollbars -----
        foreach (string scrollType in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("track", scrollType, ScrollTrackBox());
            theme.SetStylebox("scroll", scrollType, ScrollTrackBox());
            theme.SetStylebox("grabber", scrollType, ScrollGrabberBox());
            theme.SetStylebox("grabber_highlight", scrollType, ScrollGrabberActiveBox());
            theme.SetStylebox("grabber_pressed", scrollType, ScrollGrabberActiveBox());
        }

        // ----- Hairline separator -----
        theme.SetColor("separator", "HSeparator", Border);
        theme.SetConstant("separation", "HSeparator", 1);

        // ----- LineEdit (warehouse search box): flat dark input. The clear button is a separate themed button
        // (matching the game's own NSearchBar), not LineEdit.ClearButtonEnabled, so no dependency on a theme icon.
        // ----- LineEdit（仓库搜索框）：扁平深色输入框。清除按钮为独立主题按钮（与游戏 NSearchBar 一致），
        // 不用内置 ClearButtonEnabled，避免依赖主题图标。
        theme.SetStylebox("normal", "LineEdit", LineEditBox(focused: false));
        theme.SetStylebox("focus", "LineEdit", LineEditBox(focused: true));
        theme.SetColor("font_color", "LineEdit", Text);
        theme.SetColor("font_placeholder_color", "LineEdit", TextSecondary);
        theme.SetColor("caret_color", "LineEdit", Primary);
        theme.SetColor("font_selected_color", "LineEdit", Text);
        theme.SetColor("selection_color", "LineEdit", new Color(Primary.R, Primary.G, Primary.B, 0.35f));

        return theme;
    }

    private static void RegisterButtonVariation(Theme theme, string name, Color textColor,
        StyleBoxFlat normal, StyleBoxFlat hover, StyleBoxFlat pressed, int fontSize)
    {
        // Godot signature: SetTypeVariation(type_variation, base_type). "primary"/"secondary"/... are variations
        // of the built-in "Button" base type, so the variation name comes first.
        // Godot 签名：SetTypeVariation(变体名, 基类名)。primary/secondary/... 是内置 Button 的变体，变体名在前。
        theme.SetTypeVariation(name, "Button");
        theme.SetStylebox("normal", name, normal);
        theme.SetStylebox("hover", name, hover);
        theme.SetStylebox("pressed", name, pressed);
        theme.SetStylebox("focus", name, FocusBox());
        theme.SetColor("font_color", name, textColor);
        theme.SetColor("font_hover_color", name, textColor);
        theme.SetColor("font_pressed_color", name, textColor);
        theme.SetColor("font_focus_color", name, textColor);
        theme.SetFontSize("font_size", name, fontSize);
    }

    /// <summary>
    /// Resolves the vanilla font for the current locale: the base game's per-language substitute when
    /// substitution is needed (CJK etc.), Kreon otherwise. Falls back to loading Kreon by path, then to null.
    /// 解析当前语言的原版字体：需要替换时用游戏按语言提供的字体，否则用 Kreon；失败回退到 null。
    /// </summary>
    private static Font? ResolveFont(FontType type)
    {
        try
        {
            if (LocManager.Instance != null && FontManager.NeedsFontSubstitution(LocManager.Instance.Language))
            {
                Font? substitute = FontManager.GetSubstituteFont(LocManager.Instance.Language, type);
                if (substitute != null)
                {
                    return substitute;
                }
            }
        }
        catch (Exception)
        {
            // Version drift guard: fall through to a direct resource load.
        }

        try
        {
            string path = type == FontType.Bold ? "res://fonts/kreon_bold.ttf" : "res://fonts/kreon_regular.ttf";
            return ResourceLoader.Load<Font>(path, null, ResourceLoader.CacheMode.Reuse);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static StyleBoxFlat Box(Color bg, int radius = 0, Color? border = null, int borderWidth = 0,
        int shadowSize = 0)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            AntiAliasing = true,
            CornerDetail = 8,
        };
        if (radius > 0)
        {
            sb.SetCornerRadiusAll(radius);
        }

        if (border.HasValue)
        {
            sb.BorderColor = border.Value;
            if (borderWidth > 0)
            {
                sb.SetBorderWidthAll(borderWidth);
            }
        }

        if (shadowSize > 0)
        {
            sb.ShadowColor = Shadow;
            sb.ShadowSize = shadowSize;
            sb.ShadowOffset = new Vector2(0f, 2f);
        }

        return sb;
    }
}
