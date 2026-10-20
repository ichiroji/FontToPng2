using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FontToPng2.Core;

/// <summary>
/// 1文字(書記素クラスタ)をオフスクリーンで描画して画像にする。
/// HarfBuzz でシェーピングするので、絵文字の ZWJ シーケンスや国旗も1つの絵柄になる。
/// </summary>
public sealed class GlyphRenderer : IDisposable
{
    private readonly SKFont _font;
    private readonly SKFont _sdfFont; // SDF用に N倍の大きさで描くフォント
    private readonly SKShaper _shaper;
    private readonly RenderOptions _options;

    public GlyphRenderer(SKTypeface typeface, RenderOptions options)
    {
        _options = options;
        _font = new SKFont(typeface, options.FontSize)
        {
            Edging = options.Antialias ? SKFontEdging.Antialias : SKFontEdging.Alias,
            Subpixel = true,
        };
        // ヒンティングは等倍と同じ(触らない)。変えると N=1 の出力が通常画像を SDF化したものと一致しなくなる
        _sdfFont = new SKFont(typeface, options.FontSize * SdfSupersample)
        {
            Edging = _font.Edging,
            Subpixel = true,
        };
        _shaper = new SKShaper(typeface);
    }

    /// <summary>SDF済み画像を作るときの倍率</summary>
    private int SdfSupersample => Math.Clamp(_options.SdfSupersample, 1, 8);

    /// <summary>文字高 = セルの高さ(ascent〜descent から上下トリムを引いたもの。全文字で同じ)</summary>
    public int CellHeight
    {
        get
        {
            var metrics = _font.Metrics;
            return (int)Math.Ceiling(metrics.Descent - metrics.Ascent) - _options.TrimTop - _options.TrimBottom;
        }
    }

    /// <summary>フォントがこの文字の字形を持っているか</summary>
    public bool CanRender(string grapheme)
    {
        var result = _shaper.Shape(grapheme, _font);
        return result.Codepoints.Length > 0 && result.Codepoints.All(g => g != 0);
    }

    /// <summary>
    /// 文字を描画する。欠字(SkipMissing時)・空白(SkipBlank時)・幅0の文字は null を返す。
    /// </summary>
    public SKBitmap? Render(string grapheme)
    {
        var shaped = _shaper.Shape(grapheme, _font);
        if (shaped.Codepoints.Length == 0) return null;
        if (_options.SkipMissing && shaped.Codepoints.Any(g => g == 0)) return null;

        // セル = 送り幅 × (ascent〜descent)
        var metrics = _font.Metrics;
        int cellW = (int)Math.Ceiling(shaped.Width);
        int cellH = CellHeight;
        if (cellW <= 0 || cellH <= 0) return null;

        // 1) 透明なセルに文字だけを描く
        using var ink = DrawCell(grapheme, cellW, cellH, -metrics.Ascent - _options.TrimTop, _options.TextColor);

        // 2) 空白チェックと、必要ならインク境界で切り抜く（インクが無い場合はセルのまま）
        var inkBounds = _options.TrimToInk || _options.SkipBlank ? FindInkBounds(ink) : null;
        if (_options.SkipBlank && inkBounds is null && !IsWhiteSpace(grapheme)) return null;
        var src = new SKRectI(0, 0, cellW, cellH);
        if (_options.TrimToInk && inkBounds is { } bounds)
            src = bounds;

        // 3) 余白と背景を付けて最終画像にする
        int pad = _options.Padding;
        var output = new SKBitmap(new SKImageInfo(src.Width + pad * 2, src.Height + pad * 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(_options.BackgroundColor);
            canvas.ClipRect(SKRect.Create(pad, pad, src.Width, src.Height));
            using var inkImage = SKImage.FromBitmap(ink);
            canvas.DrawImage(inkImage, pad - src.Left, pad - src.Top, SKSamplingOptions.Default);
        }
        return output;
    }

    /// <summary>
    /// 文字を SDF済み画像(Gray8。画素の値 = 境界からの符号付き距離)にする。
    /// セルは Render() と同じで、その上下左右に余白 padding(全文字で同じ)を付ける。
    /// 文字を N倍(SdfSupersample)の大きさで描いてから距離場にする。
    /// null を返す条件は Render() と同じ。仕様: Yuka2/Document/SDF済み画像フォント仕様.md
    /// </summary>
    public SKBitmap? RenderSdf(string grapheme, out int padding)
    {
        int cellH = CellHeight;
        padding = SdfGenerator.Padding(cellH);

        var shaped = _shaper.Shape(grapheme, _font);
        if (shaped.Codepoints.Length == 0) return null;
        if (_options.SkipMissing && shaped.Codepoints.Any(g => g == 0)) return null;

        var metrics = _font.Metrics;
        int cellW = (int)Math.Ceiling(shaped.Width);
        if (cellW <= 0 || cellH <= 0) return null;

        float spread = SdfGenerator.Spread(cellH);
        int p = padding;
        int n = SdfSupersample;
        int hiW = (cellW + p * 2) * n;
        int hiH = (cellH + p * 2) * n;

        // 1) N倍の透明な面へ白で描く。セルの範囲で切り抜いて通常の出力と同じ形にする
        //    ベースラインは等倍のフォントの ascent × N(N倍のフォントの ascent だと通常の出力と位置がずれうる)
        using var hi = new SKBitmap(new SKImageInfo(hiW, hiH, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(hi))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = _options.Antialias })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.ClipRect(SKRect.Create(p * n, p * n, cellW * n, cellH * n));
            float baseline = (p + (-metrics.Ascent - _options.TrimTop)) * n;
            canvas.DrawShapedText(_shaper, grapheme, p * n, baseline, SKTextAlign.Left, _sdfFont, paint);
        }

        // 2) α をカバレッジにする
        var coverage = new byte[hiW * hiH];
        var px = hi.GetPixelSpan();
        int stride = hi.RowBytes;
        bool hasInk = false;
        for (int y = 0; y < hiH; y++)
        {
            int s = y * stride;
            int d = y * hiW;
            for (int x = 0; x < hiW; x++)
            {
                byte a = px[s + x * 4 + 3];
                coverage[d + x] = a;
                if (a != 0) hasInk = true;
            }
        }
        if (_options.SkipBlank && !hasInk && !IsWhiteSpace(grapheme)) return null;

        // 3) 距離値にして Gray8 の画像にする(幅が4の倍数でないと行の境目がずれるので RowBytes で1行ずつ写す)
        var sdf = SdfGenerator.BuildFromCoverage(coverage, hiW, hiH, hiW, n, spread, out int ow, out int oh);
        if (sdf is null) return null;
        var output = new SKBitmap(new SKImageInfo(ow, oh, SKColorType.Gray8, SKAlphaType.Opaque));
        var dst = output.GetPixelSpan();
        int rowBytes = output.RowBytes;
        for (int y = 0; y < oh; y++)
            sdf.AsSpan(y * ow, ow).CopyTo(dst.Slice(y * rowBytes, ow));
        return output;
    }

    /// <summary>
    /// カラーの字形(カラー絵文字など)か。文字色を変えて2回描き、
    /// 文字色に関係なく同じ色になる画素があればカラーとみなす。
    /// 単色の字形は全画素が文字色に染まるので必ず違いが出る。
    /// </summary>
    public bool IsColorGlyph(string grapheme)
    {
        var shaped = _shaper.Shape(grapheme, _font);
        var metrics = _font.Metrics;
        int w = (int)Math.Ceiling(shaped.Width);
        int h = (int)Math.Ceiling(metrics.Descent - metrics.Ascent);
        if (w <= 0 || h <= 0) return false;

        using var a = DrawCell(grapheme, w, h, -metrics.Ascent, new SKColor(255, 0, 0));
        using var b = DrawCell(grapheme, w, h, -metrics.Ascent, new SKColor(0, 0, 255));
        var pa = a.GetPixelSpan();
        var pb = b.GetPixelSpan();
        for (int i = 0; i < pa.Length; i += 4)
        {
            if (pa[i + 3] == 0) continue; // インクの無い画素は比較しない
            if (pa[i] == pb[i] && pa[i + 1] == pb[i + 1] && pa[i + 2] == pb[i + 2] && pa[i + 3] == pb[i + 3])
                return true;
        }
        return false;
    }

    private SKBitmap DrawCell(string grapheme, int w, int h, float baseline, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = color, IsAntialias = _options.Antialias };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawShapedText(_shaper, grapheme, 0, baseline, SKTextAlign.Left, _font, paint);
        return bmp;
    }

    /// <summary>描くと空白になる文字か（スペース等の空白文字は false）。SkipBlank でスキップされる文字の判定</summary>
    public bool IsBlank(string grapheme)
    {
        if (IsWhiteSpace(grapheme)) return false;
        var shaped = _shaper.Shape(grapheme, _font);
        var metrics = _font.Metrics;
        int w = (int)Math.Ceiling(shaped.Width);
        int h = (int)Math.Ceiling(metrics.Descent - metrics.Ascent);
        if (w <= 0 || h <= 0) return false;
        using var cell = DrawCell(grapheme, w, h, -metrics.Ascent, _options.TextColor);
        return FindInkBounds(cell) is null;
    }

    /// <summary>スペース・全角スペースなど、元々何も描かれない空白文字か</summary>
    private static bool IsWhiteSpace(string grapheme) =>
        grapheme.EnumerateRunes().All(Rune.IsWhiteSpace);

    /// <summary>アルファが 0 でない画素を囲む矩形。インクが無ければ null</summary>
    private static SKRectI? FindInkBounds(SKBitmap bmp)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        var pixels = bmp.GetPixelSpan();
        int w = bmp.Width, h = bmp.Height, stride = bmp.RowBytes;
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                if (pixels[row + x * 4 + 3] == 0) continue; // RGBA の A
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? null : new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    public void Dispose()
    {
        _shaper.Dispose();
        _sdfFont.Dispose();
        _font.Dispose();
    }
}
