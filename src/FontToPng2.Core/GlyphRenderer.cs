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
        _shaper = new SKShaper(typeface);
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
        int cellH = (int)Math.Ceiling(metrics.Descent - metrics.Ascent) - _options.TrimTop - _options.TrimBottom;
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
        _font.Dispose();
    }
}
