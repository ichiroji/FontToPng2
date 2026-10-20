using System.Text;
using SkiaSharp;

namespace FontToPng2.Core;

/// <summary>変換の進捗</summary>
public readonly record struct ExportProgress(int Done, int Total, int Written, int Skipped, string Current);

/// <summary>変換結果</summary>
public readonly record struct ExportResult(int Total, int Written, int Skipped, bool Canceled, string? CsvPath);

/// <summary>
/// 出力した文字の一覧(CSV)の設定。
/// 1行 = "文字コード番号,ファイル名[,1 | ,2,余白]"
/// （",1" はカラーの字形、",2,余白" は SDF済み画像のときだけ付く）
/// </summary>
public sealed record CsvListOptions
{
    /// <summary>CSV のファイル名（出力先フォルダに作る）。空なら作らない</summary>
    public string FileName { get; init; } = "FontList.csv";

    /// <summary>CSV のファイル名欄で PNG ファイル名の前に付ける文字。例: "FONT\"</summary>
    public string PathPrefix { get; init; } = "";
}

/// <summary>文字一覧を1文字1PNGで書き出す</summary>
public static class PngExporter
{
    public static ExportResult Export(
        SKTypeface typeface,
        RenderOptions options,
        IReadOnlyList<string> graphemes,
        string outputDir,
        CsvListOptions? csv = null,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancel = default)
    {
        Directory.CreateDirectory(outputDir);
        using var renderer = new GlyphRenderer(typeface, options);
        var csvLines = new StringBuilder();

        int written = 0, skipped = 0;
        bool canceled = false;
        for (int i = 0; i < graphemes.Count; i++)
        {
            if (cancel.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var g = graphemes[i];
            // 欠字・空白で飛ばす判定は SDF出力でも Render() で行う
            using (var bmp = renderer.Render(g))
            {
                if (bmp is null)
                {
                    skipped++;
                }
                else
                {
                    bool color = (csv is not null || options.SdfOutput) && renderer.IsColorGlyph(g);

                    // SDF出力ではカラーの字形だけ通常の画像にする
                    int sdfPadding = 0;
                    bool sdfGlyph = options.SdfOutput && !color;
                    using var sdf = sdfGlyph ? renderer.RenderSdf(g, out sdfPadding) : null;
                    var image = sdfGlyph ? sdf : bmp;
                    if (image is null)
                    {
                        skipped++;
                    }
                    else
                    {
                        var fileName = CharsetSource.ToFileName(g) + ".png";
                        SavePng(image, Path.Combine(outputDir, fileName));
                        written++;

                        if (csv is not null)
                        {
                            csvLines.Append(CharsetSource.ToCodeString(g)).Append(',').Append(csv.PathPrefix).Append(fileName);
                            if (color) csvLines.Append(",1");
                            else if (sdfGlyph) csvLines.Append(",2,").Append(sdfPadding);
                            csvLines.Append("\r\n");
                        }
                    }
                }
            }

            // 毎回報告すると UI が重くなるので間引く
            if (i % 16 == 0 || i == graphemes.Count - 1)
                progress?.Report(new ExportProgress(i + 1, graphemes.Count, written, skipped, g));
        }

        // キャンセルした場合も、それまでに出力した分の一覧は書く
        string? csvPath = null;
        if (csv is not null && !string.IsNullOrWhiteSpace(csv.FileName))
        {
            csvPath = Path.Combine(outputDir, csv.FileName);
            File.WriteAllText(csvPath, csvLines.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        return new ExportResult(graphemes.Count, written, skipped, canceled, csvPath);
    }

    private static void SavePng(SKBitmap bmp, string path)
    {
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
