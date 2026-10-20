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

    /// <summary>
    /// true なら SDF済み画像(Yuka2 用。8bitグレースケールの距離値)で出力する。
    /// カラーの字形は従来どおりの画像になる。仕様: Yuka2/Document/SDF済み画像フォント仕様.md
    /// </summary>
    public bool SdfOutput { get; init; } = false;

    /// <summary>SDF済み画像を作るときに文字を何倍の大きさで描くか(1〜8)。大きいほど輪郭がなめらか</summary>
    public int SdfSupersample { get; init; } = 4;
}
