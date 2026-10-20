using System.IO;
using System.Text.Json;

namespace FontToPng2.App;

/// <summary>
/// 画面の設定。%APPDATA%\FontToPng2\settings.json に保存する。
/// 数値欄は入力途中の値もそのまま戻せるよう文字列で持つ。
/// </summary>
public sealed class AppSettings
{
    // フォント
    public bool UseFontFile { get; set; } = false;
    public string FontFamily { get; set; } = "";
    public bool Bold { get; set; } = false;
    public bool Italic { get; set; } = false;
    public string FontFilePath { get; set; } = "";
    public string FontIndex { get; set; } = "0";

    // 描画
    public string FontSize { get; set; } = "64";
    public string Padding { get; set; } = "0";
    public string TrimTop { get; set; } = "0";
    public string TrimBottom { get; set; } = "0";
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#000000";
    public bool TransparentBackground { get; set; } = true;
    public bool TrimToInk { get; set; } = false;
    public bool Antialias { get; set; } = true;
    public bool SkipMissing { get; set; } = true;
    public bool SkipBlank { get; set; } = true;
    public bool SdfOutput { get; set; } = false;
    public string SdfSupersample { get; set; } = "4";

    // 出力する文字
    /// <summary>チェックを付けたプリセットの名前（CharsetSource.Presets の Name）</summary>
    public List<string> CheckedPresets { get; set; } = ["ひらがな・カタカナ"];
    /// <summary>その他の範囲</summary>
    public string Ranges { get; set; } = "";
    public string ExtraText { get; set; } = "";

    // 出力
    public string OutputDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FontToPng2");
    public string CsvFileName { get; set; } = "FontList.csv";
    public string CsvPathPrefix { get; set; } = "";

    // プレビュー
    public string PreviewText { get; set; } = "Aあ漢字😀👨‍👩‍👧🇯🇵";

    // ウィンドウ（NaN は未保存）
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 720;
    public bool WindowMaximized { get; set; } = false;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FontToPng2", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語はそのまま書く（絵文字は \u エスケープになるが読み込みに問題はない）
    };

    /// <summary>設定を読み込む。ファイルが無い・壊れている場合は初期値</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れた設定で起動できなくなるのを避け、初期値で起動する
        }
        return new AppSettings();
    }

    /// <summary>設定を保存する。失敗しても例外は投げない（戻り値で通知）</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
