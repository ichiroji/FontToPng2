using SkiaSharp;

namespace FontToPng2.Core;

/// <summary>1文字画像の描画設定</summary>
public sealed record RenderOptions
{
    /// <summary>フォントサイズ(px, em サイズ)</summary>
    public float FontSize { get; init; } = 64;

    /// <summary>文字色（カラー絵文字には影響しない）</summary>
    public SKColor TextColor { get; init; } = SKColors.White;

    /// <summary>背景色。Alpha=0 なら透明背景</summary>
    public SKColor BackgroundColor { get; init; } = SKColors.Transparent;

    /// <summary>画像の周囲に付ける余白(px)</summary>
    public int Padding { get; init; } = 0;

    /// <summary>セルの上端から切り落とす量(px)。旧版の trimming_top 相当</summary>
    public int TrimTop { get; init; } = 0;

    /// <summary>セルの下端から切り落とす量(px)。旧版の trimming_bottom 相当</summary>
    public int TrimBottom { get; init; } = 0;

    /// <summary>true ならインク(実際に描かれた部分)の境界で切り抜く</summary>
    public bool TrimToInk { get; init; } = false;

    /// <summary>true ならフォントに無い文字を出力しない</summary>
    public bool SkipMissing { get; init; } = true;

    /// <summary>
    /// true なら描いた結果が空白（インクが1画素も無い）の文字を出力しない。
    /// スペースなどの空白文字は元々空白なので対象外。
    /// 字形はあるのに描けない形式のフォント(COLRv1 など)で空の画像ができるのを防ぐ。
    /// </summary>
    public bool SkipBlank { get; init; } = true;

    /// <summary>アンチエイリアス</summary>
    public bool Antialias { get; init; } = true;
}
