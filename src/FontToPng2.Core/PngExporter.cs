using System.Text;
using SkiaSharp;

namespace FontToPng2.Core;

/// <summary>変換の進捗</summary>
public readonly record struct ExportProgress(int Done, int Total, int Written, int Skipped, string Current);

/// <summary>変換結果</summary>
public readonly record struct ExportResult(int Total, int Written, int Skipped, bool Canceled, string? CsvPath);

/// <summary>
/// 出力した文字の一覧(CSV)の設定。
/// 1行 = "文字コード番号,ファイル名[,1]"（3列目はカラーの字形のときだけ付く）
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
            using (var bmp = renderer.Render(g))
            {
                if (bmp is null)
                {
                    skipped++;
                }
                else
                {
                    var fileName = CharsetSource.ToFileName(g) + ".png";
                    using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
                    using (var fs = File.Create(Path.Combine(outputDir, fileName)))
                        data.SaveTo(fs);
                    written++;

                    if (csv is not null)
                    {
                        csvLines.Append(CharsetSource.ToCodeString(g)).Append(',').Append(csv.PathPrefix).Append(fileName);
                        if (renderer.IsColorGlyph(g)) csvLines.Append(",1");
                        csvLines.Append("\r\n");
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
}
