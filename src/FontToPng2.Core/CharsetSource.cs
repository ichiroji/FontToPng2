using System.Globalization;

namespace FontToPng2.Core;

/// <summary>出力する文字(書記素クラスタ)の一覧を作る</summary>
public static class CharsetSource
{
    /// <summary>
    /// 文字範囲のプリセット（旧版の決め打ち範囲を分類したもの＋絵文字）。
    /// Name は設定ファイルにチェック状態を保存するときのキーも兼ねる。
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Spec)> Presets =
    [
        ("英数字", "0020-007E"),
        ("記号", "00D7, 00F7, 2000-2BFF, 3000-303F"),
        ("ひらがな・カタカナ", "3040-30FF"),
        ("漢字", "4E00-9FFF, F900-FAFF"),
        ("全角英数・半角カナ", "FE30-FE6F, FF00-FF9F"),
        ("絵文字", "2600-27BF, 1F300-1F5FF, 1F600-1F64F, 1F680-1F6FF, 1F900-1F9FF, 1FA70-1FAFF"),
    ];

    /// <summary>旧版と同じ範囲になるプリセット（絵文字以外の全部）</summary>
    public static readonly IReadOnlyList<string> LegacyPresetNames =
        ["英数字", "記号", "ひらがな・カタカナ", "漢字", "全角英数・半角カナ"];

    /// <summary>範囲指定に含まれる文字数（重複は数えない）</summary>
    public static int CountRanges(string spec) => FromRanges(spec).Distinct().Count();

    /// <summary>
    /// 範囲指定文字列を解釈する。例: "3040-30FF, 4E00-9FFF, U+1F600"
    /// 区切りはカンマ・空白・改行。
    /// </summary>
    public static IEnumerable<string> FromRanges(string spec)
    {
        var tokens = spec.Split([',', ' ', '\t', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var parts = token.Split('-');
            int start, end;
            if (parts.Length == 1)
            {
                start = end = ParseCodePoint(parts[0], token);
            }
            else if (parts.Length == 2)
            {
                start = ParseCodePoint(parts[0], token);
                end = ParseCodePoint(parts[1], token);
                if (end < start) throw new FormatException($"範囲の開始と終了が逆です: \"{token}\"");
            }
            else
            {
                throw new FormatException($"範囲の書式が正しくありません: \"{token}\"");
            }

            for (int cp = start; cp <= end; cp++)
            {
                if (cp is >= 0xD800 and <= 0xDFFF) continue; // サロゲート領域は文字ではない
                yield return char.ConvertFromUtf32(cp);
            }
        }
    }

    /// <summary>テキストを書記素クラスタ単位に分割する（絵文字の ZWJ シーケンスや国旗も1文字扱い）</summary>
    public static IEnumerable<string> FromText(string text)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var s = e.GetTextElement();
            if (s.All(char.IsControl)) continue; // 改行・タブなどは除外
            yield return s;
        }
    }

    /// <summary>
    /// 既存のテキストに文字を追記し、重複（と改行・タブ）を除いたテキストを返す。
    /// Added は新たに増えた文字数。
    /// </summary>
    public static (string Merged, int Added) MergeUnique(string existing, string addition)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sb = new System.Text.StringBuilder();
        foreach (var s in FromText(existing))
            if (seen.Add(s)) sb.Append(s);
        int before = seen.Count;
        foreach (var s in FromText(addition))
            if (seen.Add(s)) sb.Append(s);
        return (sb.ToString(), seen.Count - before);
    }

    /// <summary>範囲指定と追加テキストを合わせ、重複を除いて順序を保った一覧を返す</summary>
    public static IReadOnlyList<string> Build(string rangeSpec, string extraText)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var s in FromRanges(rangeSpec).Concat(FromText(extraText)))
        {
            if (seen.Add(s)) list.Add(s);
        }
        return list;
    }

    /// <summary>出力ファイル名(拡張子なし)。旧版と同じくゼロ埋めなし。例: "20"、"3042"、"1f468-200d-1f469"</summary>
    public static string ToFileName(string grapheme) =>
        string.Join("-", grapheme.EnumerateRunes().Select(r => r.Value.ToString("x")));

    /// <summary>CSV に書く文字コード番号。例: "0x20"、"0x1f468:0x200d:0x1f469"</summary>
    public static string ToCodeString(string grapheme) =>
        string.Join(":", grapheme.EnumerateRunes().Select(r => "0x" + r.Value.ToString("x")));

    private static int ParseCodePoint(string s, string token)
    {
        s = s.Trim();
        if (s.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) || cp < 0 || cp > 0x10FFFF)
            throw new FormatException($"コードポイントを解釈できません: \"{token}\"（16進数で指定してください）");
        return cp;
    }
}
