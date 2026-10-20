using SkiaSharp;

namespace FontToPng2.Core;

/// <summary>フォント(SKTypeface)の取得</summary>
public static class FontLoader
{
    /// <summary>インストール済みフォントのファミリー名一覧</summary>
    public static IReadOnlyList<string> GetSystemFamilies() =>
        SKFontManager.Default.FontFamilies
            .Distinct()
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>インストール済みフォントから取得</summary>
    public static SKTypeface FromSystem(string family, bool bold, bool italic)
    {
        var style = new SKFontStyle(
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        return SKTypeface.FromFamilyName(family, style)
            ?? throw new InvalidOperationException($"フォント \"{family}\" を読み込めません。");
    }

    /// <summary>
    /// カラーフォントとして正しく描けない形式なら注意文を返す。問題なければ null。
    /// Windows 版 SkiaSharp (DirectWrite) は COLRv0 と CBDT/sbix(PNG) は描けるが、
    /// COLRv1 と SVG-in-OpenType は描けない（空白または白黒になる）。
    /// </summary>
    public static string? GetColorFontWarning(SKTypeface typeface)
    {
        var tags = typeface.GetTableTags().ToHashSet();
        if (tags.Contains(Tag("CBDT")) || tags.Contains(Tag("sbix"))) return null;

        if (tags.Contains(Tag("COLR")) && typeface.TryGetTableData(Tag("COLR"), out var colr) && colr.Length >= 4)
        {
            int version = (colr[0] << 8) | colr[1];
            int numBaseGlyphRecords = (colr[2] << 8) | colr[3];
            if (version == 0 || numBaseGlyphRecords > 0) return null; // COLRv0 の絵柄がある
            return "このフォントは COLRv1 形式のため、カラー絵文字が描画されません。CBDT 版（例: Noto Color Emoji の NotoColorEmoji.ttf）を使ってください。";
        }

        if (tags.Contains(Tag("SVG ")))
            return "このフォントは SVG 形式のカラーフォントのため、白黒で描画されます。";

        return null;
    }

    private static uint Tag(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);

    /// <summary>フォントファイル(TTF/OTF/TTC)から取得。index は TTC 内の番号</summary>
    public static SKTypeface FromFile(string path, int index = 0) =>
        SKTypeface.FromFile(path, index)
            ?? throw new InvalidOperationException($"フォントファイル \"{path}\" (番号 {index}) を読み込めません。");
}
