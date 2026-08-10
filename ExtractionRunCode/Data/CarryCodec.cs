using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ExtractionRun.Data;

/// <summary>
/// Encodes a carry config as a shareable, human-readable gear code and parses it back. Format:
/// <c>3x STRIKE_IRONCLAD + 1x MADSCIENCE[WHAT_IF_RELICS] + 500G#F7K2</c> — items joined by <c> + </c> in the fixed
/// order cards → relics → potions → gold, mod-owned items annotated with their normalized mod-id stem in brackets, and a
/// trailing <c>#checksum</c> (FNV-1a over the normalized body) so a hand-typed or truncated code fails loudly instead of
/// mis-parsing. Encoding is deterministic: identical carries always produce identical codes. The code carries only ids +
/// ownership, never an item-kind marker or saved props — the receiving side resolves each entry's kind from ModelDb, and
/// identity-card props are re-derived on import from the receiving warehouse.
/// 把携带配置编码成可分享的可读战备码并解析回来：物品以 <c> + </c> 连接（固定顺序 卡→遗物→药水→金币），mod 物品用方括号
/// 标注规范化 mod-id，末尾 <c>#校验和</c>（对归一化正文的 FNV-1a）让手抄/截断的码响亮失败而非错解。编码确定：相同携带
/// 永远生成相同码。码只携带 id + 归属，不含物品种类标记或 Props——接收端按 ModelDb 解析每种 entry 的类别，身份牌
/// Props 在导入时按接收者仓库重建。
/// </summary>
public static class CarryCodec
{
    /// <summary>Max per-item count the parser accepts; import clamps against warehouse stock regardless. 解析接受的单品数量上限。</summary>
    public const int MaxCount = 999;

    /// <summary>Checksum alphabet (Crockford-like, no I/L/O/U): 5 bits per char, typo-hostile letters excluded. 校验和字母表。</summary>
    private const string ChecksumAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int ChecksumChars = 4;

    /// <summary>The three carryable item kinds. 可携带的三种物品种类。</summary>
    public enum ItemKind { Card, Relic, Potion }

    /// <summary>One item in a code: its entry id, an optional normalized mod-id stem annotation, and the count.
    /// The kind is not stored — it is resolved against ModelDb at import time. 码中的单个物品（不含类别，导入时解析）。</summary>
    public sealed record CodeItem(string Entry, string? OwnerStem, int Count);

    /// <summary>A decoded gear code: its items plus gold. 解析出的战备码：物品 + 金币。</summary>
    public sealed class DecodedCarry
    {
        public List<CodeItem> Items { get; } = new();
        public int Gold { get; set; }
    }

    /// <summary>Why a code failed to parse. 战备码解析失败的原因。</summary>
    public enum DecodeError { None, Empty, MissingChecksum, BadChecksum, BadSegment, CountOverflow }

    // ----- Encode 编码 -----

    /// <summary>
    /// Encodes a carry into a gear code. Items are ordered cards → relics → potions → gold, sorted by entry within each
    /// kind and merged by count, so identical carries always produce identical codes. <paramref name="resolveOwner"/>
    /// maps each item id to its normalized mod-id stem (or null for base content) for the bracket annotation.
    /// 把携带编码成战备码：卡→遗物→药水→金币，同类内按 entry 排序并合并数量，相同携带永远生成相同码。
    /// </summary>
    public static string Encode(CarryConfig carry, Func<ItemKind, ModelId, string?> resolveOwner)
    {
        var segments = new List<string>();
        AppendSegments(segments, carry.Cards, ItemKind.Card, static c => c.Card.Id, resolveOwner);
        AppendSegments(segments, carry.Relics, ItemKind.Relic, static r => r.Relic.Id, resolveOwner);
        AppendSegments(segments, carry.Potions, ItemKind.Potion, static p => p.Id, resolveOwner);

        if (carry.Gold > 0)
        {
            segments.Add($"{carry.Gold}G");
        }

        // Checksum over the canonical (spaceless, uppercased) body; the display form below only adds readability spacing.
        // 校验和对规范正文（去空白、大写）计算；展示形式只加可读空格。
        string body = string.Join("+", segments);
        string checksum = ComputeChecksum(body.ToUpperInvariant());
        return string.Join(" + ", segments.Select(Prettify)) + "#" + checksum;
    }

    /// <summary>Merges same-entry copies into one sorted <c>{count}x{entry}[{owner}]</c> segment. 合并同 entry 的重复并为一段。</summary>
    private static void AppendSegments<T>(List<string> segments, IEnumerable<T> items, ItemKind kind,
        Func<T, ModelId?> idOf, Func<ItemKind, ModelId, string?> resolveOwner)
    {
        var counts = new Dictionary<string, (string? Owner, int Count)>();
        foreach (T item in items)
        {
            ModelId? id = idOf(item);
            if (id == null || id.Entry.Length == 0)
            {
                continue;
            }

            string? owner = resolveOwner(kind, id);
            counts.TryGetValue(id.Entry, out (string? Owner, int Count) existing);
            counts[id.Entry] = (owner, existing.Count + 1);
        }

        foreach (KeyValuePair<string, (string? Owner, int Count)> kv in
                 counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string segment = $"{kv.Value.Count}x{kv.Key}";
            if (kv.Value.Owner != null)
            {
                segment += $"[{kv.Value.Owner}]";
            }

            segments.Add(segment);
        }
    }

    /// <summary>Adds the readability space after the count marker ("3xSTRIKE" → "3x STRIKE"); gold segments are unchanged.
    /// 在数量标记后加可读空格；金币段不变。</summary>
    private static string Prettify(string segment)
    {
        int x = segment.IndexOf('x');
        return x > 0 ? segment.Insert(x + 1, " ") : segment;
    }

    // ----- Decode 解码 -----

    /// <summary>
    /// Parses a gear code back into items + gold. The input is normalized (uppercase, whitespace stripped) so hand-typed
    /// or re-spaced codes still verify; the trailing <c>#checksum</c> is validated against that normalized body.
    /// 解析战备码：输入先归一（大写、去空白）再解析，末尾 '#校验和' 与归一后的正文比对。
    /// </summary>
    public static bool TryDecode(string text, out DecodedCarry carry, out DecodeError error)
    {
        carry = new DecodedCarry();
        error = DecodeError.None;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = DecodeError.Empty;
            return false;
        }

        string normalized = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        int hashIndex = normalized.IndexOf('#');
        if (hashIndex < 1 || hashIndex == normalized.Length - 1)
        {
            error = DecodeError.MissingChecksum;
            return false;
        }

        if (normalized.IndexOf('#', hashIndex + 1) >= 0)
        {
            error = DecodeError.BadChecksum;
            return false;
        }

        string body = normalized.Substring(0, hashIndex);
        string provided = normalized.Substring(hashIndex + 1);
        if (provided != ComputeChecksum(body))
        {
            error = DecodeError.BadChecksum;
            return false;
        }

        foreach (string raw in body.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseSegment(raw, carry, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSegment(string segment, DecodedCarry carry, out DecodeError error)
    {
        error = DecodeError.None;

        // Gold: digits followed by G. Checked first so "500G" is never read as an item (item entries contain letters).
        // 金币：数字 + G。优先判断，避免把金币误当物品。
        if (segment.Length >= 2 && segment[^1] == 'G' && IsDigits(segment.AsSpan(0, segment.Length - 1)))
        {
            carry.Gold += int.Parse(segment.AsSpan(0, segment.Length - 1));
            return true;
        }

        // Item: <count>x<entry>[<owner>].
        int x = segment.IndexOf('X');
        if (x <= 0 || !int.TryParse(segment.AsSpan(0, x), out int count) || count < 1)
        {
            error = DecodeError.BadSegment;
            return false;
        }

        if (count > MaxCount)
        {
            error = DecodeError.CountOverflow;
            return false;
        }

        string rest = segment.Substring(x + 1);
        string? owner = null;
        string entry;
        int bracket = rest.IndexOf('[');
        if (bracket >= 0)
        {
            if (!rest.EndsWith(']'))
            {
                error = DecodeError.BadSegment;
                return false;
            }

            owner = rest.Substring(bracket + 1, rest.Length - bracket - 2);
            entry = rest.Substring(0, bracket);
        }
        else
        {
            entry = rest;
        }

        if (entry.Length == 0 || !entry.All(IsIdChar))
        {
            error = DecodeError.BadSegment;
            return false;
        }

        if (owner != null && owner.Length > 0 && !owner.All(IsIdChar))
        {
            error = DecodeError.BadSegment;
            return false;
        }

        carry.Items.Add(new CodeItem(entry, owner, count));
        return true;
    }

    private static bool IsDigits(ReadOnlySpan<char> span)
    {
        foreach (char c in span)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return span.Length > 0;
    }

    /// <summary>
    /// A valid id character is anything except the code's structural symbols (<c>+</c> item separator, <c>[</c>/<c>]</c>
    /// owner brackets, <c>#</c> checksum marker) and whitespace. Base-game entries are slugified (letters/digits/underscore
    /// only), but mod frameworks set arbitrary public entries — e.g. YuWanCard uses <c>-</c> as a compound separator
    /// (<c>YUWANCARD-PIG_ROAST_PORK</c>) — so rejecting anything outside <c>[A-Z0-9_]</c> breaks legit codes.
    /// 合法 id 字符 = 除语法符号（+ 分隔、[ ] 归属、# 校验）与空白外的任意字符。基础内容 entry 是 slug 化的（仅字母/数字/下划线），
    /// 但 mod 框架会设置任意的公开 entry——如 YuWanCard 用 - 作为复合分隔（YUWANCARD-PIG_ROAST_PORK）——拒绝 [A-Z0-9_] 以外会弄坏合法码。
    /// </summary>
    private static bool IsIdChar(char c) => !char.IsWhiteSpace(c) && c is not '+' and not '[' and not ']' and not '#';

    /// <summary>FNV-1a 32-bit over the normalized body, rendered as 4 base32 chars (20 bits). FNV-1a 哈希渲染为 4 个 base32 字符。</summary>
    private static string ComputeChecksum(string body)
    {
        uint hash = 2166136261u;
        foreach (byte b in Encoding.UTF8.GetBytes(body))
        {
            hash ^= b;
            hash *= 16777619u;
        }

        var chars = new char[ChecksumChars];
        for (int i = 0; i < ChecksumChars; i++)
        {
            chars[i] = ChecksumAlphabet[(int)((hash >> (5 * i)) & 31)];
        }

        return new string(chars);
    }
}
