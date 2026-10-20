using System.Globalization;
using System.IO;
using System.Windows;
using FontToPng2.Core;
using RenderOptions = FontToPng2.Core.RenderOptions;
using Microsoft.Win32;
using SkiaSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FontToPng2.App;

public partial class MainWindow : Window
{
    private bool _ready;
    private CancellationTokenSource? _cts;

    // フォントの作り直しを避けるためのキャッシュ
    private string? _typefaceKey;
    private SKTypeface? _typeface;

    public MainWindow()
    {
        InitializeComponent();

        FamilyCombo.ItemsSource = FontLoader.GetSystemFamilies();
        CreatePresetCheckBoxes();

        ApplySettings(AppSettings.Load());

        _ready = true;
        UpdateCharCount();
        UpdatePreview();
    }

    // ---------------- 設定の保存と復元 ----------------

    private void ApplySettings(AppSettings s)
    {
        // フォント：保存したフォントがアンインストールされていたら既定のフォントにする
        var families = (IReadOnlyList<string>)FamilyCombo.ItemsSource;
        FamilyCombo.SelectedItem = new[] { s.FontFamily, "游ゴシック", "Yu Gothic", "Meiryo UI" }.FirstOrDefault(families.Contains)
            ?? families.FirstOrDefault();
        BoldCheck.IsChecked = s.Bold;
        ItalicCheck.IsChecked = s.Italic;
        FontPathBox.Text = s.FontFilePath;
        FontIndexBox.Text = s.FontIndex;
        // フォントファイルが消えていたらインストール済みフォントに戻す
        if (s.UseFontFile && File.Exists(s.FontFilePath)) FileFontRadio.IsChecked = true;
        else SystemFontRadio.IsChecked = true;

        SizeBox.Text = s.FontSize;
        PaddingBox.Text = s.Padding;
        TrimTopBox.Text = s.TrimTop;
        TrimBottomBox.Text = s.TrimBottom;
        TextColorBox.Text = s.TextColor;
        BgColorBox.Text = s.BackgroundColor;
        TransparentCheck.IsChecked = s.TransparentBackground;
        TrimInkCheck.IsChecked = s.TrimToInk;
        AntialiasCheck.IsChecked = s.Antialias;
        SkipMissingCheck.IsChecked = s.SkipMissing;
        SkipBlankCheck.IsChecked = s.SkipBlank;
        SdfCheck.IsChecked = s.SdfOutput;
        SupersampleCombo.SelectedItem = SupersampleItems.FirstOrDefault(i => (string)i.Content == s.SdfSupersample.Trim())
            ?? SupersampleItems.First(i => (string)i.Content == "4");
        UpdateSdfControls();

        SetCheckedPresets(s.CheckedPresets);
        RangeBox.Text = s.Ranges;
        ExtraTextBox.Text = s.ExtraText;
        OutputDirBox.Text = s.OutputDir;
        CsvNameBox.Text = s.CsvFileName;
        CsvPrefixBox.Text = s.CsvPathPrefix;
        PreviewTextBox.Text = s.PreviewText;

        // ウィンドウ：モニター構成が変わって画面外になる場合は位置を復元しない
        Width = Math.Max(s.WindowWidth, MinWidth);
        Height = Math.Max(s.WindowHeight, MinHeight);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop)
            && screen.IntersectsWith(new Rect(s.WindowLeft, s.WindowTop, Width, 40))) // タイトルバーが見えるか
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private AppSettings CollectSettings()
    {
        // 最大化中は元に戻したときの位置・サイズを保存する
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        return new AppSettings
        {
            UseFontFile = FileFontRadio.IsChecked == true,
            FontFamily = (FamilyCombo.SelectedItem as string ?? FamilyCombo.Text).Trim(),
            Bold = BoldCheck.IsChecked == true,
            Italic = ItalicCheck.IsChecked == true,
            FontFilePath = FontPathBox.Text,
            FontIndex = FontIndexBox.Text,

            FontSize = SizeBox.Text,
            Padding = PaddingBox.Text,
            TrimTop = TrimTopBox.Text,
            TrimBottom = TrimBottomBox.Text,
            TextColor = TextColorBox.Text,
            BackgroundColor = BgColorBox.Text,
            TransparentBackground = TransparentCheck.IsChecked == true,
            TrimToInk = TrimInkCheck.IsChecked == true,
            Antialias = AntialiasCheck.IsChecked == true,
            SkipMissing = SkipMissingCheck.IsChecked == true,
            SkipBlank = SkipBlankCheck.IsChecked == true,
            SdfOutput = SdfCheck.IsChecked == true,
            SdfSupersample = (string)((System.Windows.Controls.ComboBoxItem)SupersampleCombo.SelectedItem).Content,

            CheckedPresets = GetCheckedPresets(),
            Ranges = RangeBox.Text,
            ExtraText = ExtraTextBox.Text,
            OutputDir = OutputDirBox.Text,
            CsvFileName = CsvNameBox.Text,
            CsvPathPrefix = CsvPrefixBox.Text,
            PreviewText = PreviewTextBox.Text,

            WindowLeft = bounds.Left,
            WindowTop = bounds.Top,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
        };
    }

    private void OnResetSettings(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "すべての設定を初期値に戻しますか？", "設定のリセット",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _ready = false;
        var current = CollectSettings();
        var defaults = new AppSettings
        {
            // ウィンドウの位置・大きさは今のまま
            WindowLeft = current.WindowLeft, WindowTop = current.WindowTop,
            WindowWidth = current.WindowWidth, WindowHeight = current.WindowHeight,
            WindowMaximized = current.WindowMaximized,
        };
        ApplySettings(defaults);
        _ready = true;
        UpdateCharCount();
        UpdatePreview();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CollectSettings().Save();
        base.OnClosing(e);
    }

    // ---------------- 設定の読み取り ----------------

    /// <summary>SDF出力でのサイズ×倍率の上限。N倍で描く面と距離変換の作業領域が大きくなりすぎないようにする</summary>
    private const float MaxSdfScaledSize = 4096;

    /// <summary>UI の内容から RenderOptions を作る。不正な値なら例外(FormatException)</summary>
    private RenderOptions ReadOptions()
    {
        // SDF出力では文字高と余白を全文字でそろえる約束なので、余白・切り抜きは使わない。
        // 色も使わない(カラーの字形は透明背景の通常画像で出す)。無効にした欄は解析しない
        bool sdf = SdfCheck.IsChecked == true;
        float fontSize = ParseFloat(SizeBox.Text, "サイズ", 1, 2048);
        int supersample = SelectedSupersample;
        if (sdf && fontSize * supersample > MaxSdfScaledSize)
            throw new FormatException($"SDFで出力するときは サイズ×倍率 を {MaxSdfScaledSize} 以下にしてください。");
        return new RenderOptions
        {
            FontSize = fontSize,
            Padding = sdf ? 0 : ParseInt(PaddingBox.Text, "余白", 0, 1024),
            TrimTop = ParseInt(TrimTopBox.Text, "上トリム", 0, 1024),
            TrimBottom = ParseInt(TrimBottomBox.Text, "下トリム", 0, 1024),
            TextColor = sdf ? SKColors.White : ParseColor(TextColorBox.Text, "文字色"),
            BackgroundColor = sdf || TransparentCheck.IsChecked == true ? SKColors.Transparent : ParseColor(BgColorBox.Text, "背景色"),
            TrimToInk = !sdf && TrimInkCheck.IsChecked == true,
            Antialias = AntialiasCheck.IsChecked == true,
            SkipMissing = SkipMissingCheck.IsChecked == true,
            SkipBlank = SkipBlankCheck.IsChecked == true,
            SdfOutput = sdf,
            SdfSupersample = supersample,
        };
    }

    private IEnumerable<System.Windows.Controls.ComboBoxItem> SupersampleItems =>
        SupersampleCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>();

    private int SelectedSupersample =>
        int.Parse((string)((System.Windows.Controls.ComboBoxItem)SupersampleCombo.SelectedItem).Content, CultureInfo.InvariantCulture);

    /// <summary>SDF出力では使わない欄を無効にする</summary>
    private void UpdateSdfControls()
    {
        bool sdf = SdfCheck.IsChecked == true;
        foreach (var c in new UIElement[] { TextColorBox, BgColorBox, TransparentCheck, PaddingBox, TrimInkCheck })
            c.IsEnabled = !sdf;
        SupersampleCombo.IsEnabled = sdf;
    }

    /// <summary>UI の内容からフォントを得る（同じ指定ならキャッシュを返す）</summary>
    private SKTypeface ReadTypeface()
    {
        string key;
        Func<SKTypeface> create;
        if (FileFontRadio.IsChecked == true)
        {
            var path = FontPathBox.Text;
            if (string.IsNullOrEmpty(path)) throw new FormatException("フォントファイルを選択してください。");
            int index = ParseInt(FontIndexBox.Text, "TTC番号", 0, 255);
            key = $"file|{path}|{index}";
            create = () => FontLoader.FromFile(path, index);
        }
        else
        {
            // 編集可能な ComboBox は SelectionChanged の時点ではまだ Text が古い値のままなので、
            // 一覧から選ばれていれば SelectedItem を優先する（手入力で一覧に無い名前なら Text）
            var family = (FamilyCombo.SelectedItem as string ?? FamilyCombo.Text).Trim();
            if (family.Length == 0) throw new FormatException("フォントを選択してください。");
            bool bold = BoldCheck.IsChecked == true, italic = ItalicCheck.IsChecked == true;
            key = $"sys|{family}|{bold}|{italic}";
            create = () => FontLoader.FromSystem(family, bold, italic);
        }

        if (key != _typefaceKey || _typeface is null)
        {
            _typeface?.Dispose();
            _typeface = null;
            _typeface = create();
            _typefaceKey = key;
        }
        return _typeface;
    }

    private static float ParseFloat(string s, string name, float min, float max) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max
            ? v : throw new FormatException($"{name} は {min}〜{max} の数値で指定してください。");

    private static int ParseInt(string s, string name, int min, int max) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max
            ? v : throw new FormatException($"{name} は {min}〜{max} の整数で指定してください。");

    private static SKColor ParseColor(string s, string name) =>
        SKColor.TryParse(s.Trim(), out var c)
            ? c : throw new FormatException($"{name} は #RRGGBB または #AARRGGBB で指定してください。");

    // ---------------- イベント ----------------

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        UpdateSdfControls();
        UpdatePreview();
    }

    private void OnCharsetChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        UpdateCharCount();
    }

    private void UpdateCharCount()
    {
        try
        {
            var list = BuildCharset();
            CharCountText.Text = $"{list.Count:N0} 文字";
            CharCountText.Foreground = System.Windows.Media.Brushes.Gray;
        }
        catch (FormatException ex)
        {
            CharCountText.Text = ex.Message;
            CharCountText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private void OnBrowseFont(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "フォントファイル (*.ttf;*.otf;*.ttc;*.otc)|*.ttf;*.otf;*.ttc;*.otc|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        FontPathBox.Text = dlg.FileName;
        FileFontRadio.IsChecked = true;
        UpdatePreview();
    }

    // ---------------- 文字範囲のプリセット ----------------

    /// <summary>プリセットごとのチェックボックスを作る。Tag にプリセット名を入れる</summary>
    private void CreatePresetCheckBoxes()
    {
        foreach (var (name, spec) in CharsetSource.Presets)
        {
            var cb = new System.Windows.Controls.CheckBox
            {
                Content = $"{name}（{CharsetSource.CountRanges(spec):N0}）",
                Tag = name,
                ToolTip = spec,
                Margin = new Thickness(0, 2, 8, 2),
            };
            cb.Checked += OnCharsetChanged;
            cb.Unchecked += OnCharsetChanged;
            PresetPanel.Children.Add(cb);
        }
    }

    private IEnumerable<System.Windows.Controls.CheckBox> PresetCheckBoxes =>
        PresetPanel.Children.OfType<System.Windows.Controls.CheckBox>();

    private List<string> GetCheckedPresets() =>
        PresetCheckBoxes.Where(cb => cb.IsChecked == true).Select(cb => (string)cb.Tag).ToList();

    private void SetCheckedPresets(IEnumerable<string> names)
    {
        var set = names.ToHashSet();
        foreach (var cb in PresetCheckBoxes) cb.IsChecked = set.Contains((string)cb.Tag);
        UpdateCharCount();
    }

    /// <summary>チェックしたプリセット（一覧の順）＋その他の範囲＋直接指定の文字から出力する文字を作る</summary>
    private IReadOnlyList<string> BuildCharset()
    {
        var checkedNames = GetCheckedPresets().ToHashSet();
        var specs = CharsetSource.Presets.Where(p => checkedNames.Contains(p.Name)).Select(p => p.Spec)
            .Append(RangeBox.Text);
        return CharsetSource.Build(string.Join(", ", specs), ExtraTextBox.Text);
    }

    private void OnSelectAllPresets(object sender, RoutedEventArgs e) => SetCheckedPresets(CharsetSource.Presets.Select(p => p.Name));

    private void OnClearPresets(object sender, RoutedEventArgs e) => SetCheckedPresets([]);

    private void OnLoadTextFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        // 今の内容に追記し、重複を除く。文字コードはファイルごとに自動判別する
        var text = ExtraTextBox.Text;
        var report = new List<string>();
        foreach (var path in dlg.FileNames)
        {
            try
            {
                var (content, encoding) = TextFileReader.Read(path);
                (text, int added) = CharsetSource.MergeUnique(text, content);
                report.Add($"{Path.GetFileName(path)}（{encoding}）: {added:N0} 文字追加");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        ExtraTextBox.Text = text;
        StatusText.Text = string.Join("\n", report);
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (Directory.Exists(OutputDirBox.Text)) dlg.InitialDirectory = OutputDirBox.Text;
        if (dlg.ShowDialog(this) == true) OutputDirBox.Text = dlg.FolderName;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        SKTypeface typeface;
        RenderOptions options;
        IReadOnlyList<string> chars;
        CsvListOptions csv;
        try
        {
            typeface = ReadTypeface();
            options = ReadOptions();
            chars = BuildCharset();
            if (chars.Count == 0) throw new FormatException("出力する文字がありません。");
            if (string.IsNullOrWhiteSpace(OutputDirBox.Text)) throw new FormatException("出力先フォルダを指定してください。");
            var csvName = CsvNameBox.Text.Trim();
            if (csvName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new FormatException("一覧CSVのファイル名に使えない文字が含まれています。");
            csv = new CsvListOptions { FileName = csvName, PathPrefix = CsvPrefixBox.Text };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "設定エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CollectSettings().Save(); // 変換中に落ちても設定が残るように
        var outDir = OutputDirBox.Text;
        _cts = new CancellationTokenSource();
        SetRunning(true);
        Progress.Maximum = chars.Count;
        Progress.Value = 0;

        var progress = new Progress<ExportProgress>(p =>
        {
            Progress.Value = p.Done;
            StatusText.Text = $"{p.Done:N0} / {p.Total:N0}（出力 {p.Written:N0}、スキップ {p.Skipped:N0}）  {p.Current}";
        });

        try
        {
            var token = _cts.Token;
            var result = await Task.Run(() => PngExporter.Export(typeface, options, chars, outDir, csv, progress, token));
            StatusText.Text = (result.Canceled ? "キャンセルしました。" : "完了しました。")
                + $" 出力 {result.Written:N0} 枚、スキップ {result.Skipped:N0} 文字"
                + (result.CsvPath is { } csvPath ? $"\n一覧: {csvPath}" : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = "エラー: " + ex.Message;
            MessageBox.Show(this, ex.Message, "出力エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetRunning(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetRunning(bool running)
    {
        StartButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
    }

    // ---------------- プレビュー ----------------

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreview();

    /// <summary>プレビュー領域と同じ物理ピクセル数の画像に描き、等倍で表示する</summary>
    private void UpdatePreview()
    {
        if (!_ready) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)(PreviewHost.ActualWidth * dpi.DpiScaleX);
        int h = (int)(PreviewHost.ActualHeight * dpi.DpiScaleY);
        if (w <= 0 || h <= 0) return;

        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            DrawPreview(canvas, w);
        }

        var wb = new WriteableBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), bmp.GetPixels(), bmp.ByteCount, bmp.RowBytes);
        PreviewImage.Source = wb;
    }

    private void DrawPreview(SKCanvas canvas, int canvasWidth)
    {
        SKTypeface typeface;
        RenderOptions options;
        try
        {
            typeface = ReadTypeface();
            // スキップされる文字も印を付けて見せるため、プレビューではスキップしない
            options = ReadOptions() with { SkipMissing = false, SkipBlank = false };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            PreviewInfo.Text = ex.Message;
            return;
        }

        using var renderer = new GlyphRenderer(typeface, options);
        using var frame = new SKPaint { Color = new SKColor(0, 120, 215), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var missing = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var blankMark = new SKPaint
        {
            Color = SKColors.Orange, Style = SKPaintStyle.Stroke, StrokeWidth = 3,
            PathEffect = SKPathEffect.CreateDash([6, 4], 0),
        };
        using var sdfCell = new SKPaint
        {
            Color = new SKColor(0, 200, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 1,
            PathEffect = SKPathEffect.CreateDash([4, 3], 0),
        };
        bool skipBlank = SkipBlankCheck.IsChecked == true;

        const float gap = 12;
        float x = gap, y = gap, rowH = 0;
        int missingCount = 0, blankCount = 0;
        foreach (var g in CharsetSource.FromText(PreviewTextBox.Text))
        {
            bool has = renderer.CanRender(g);
            // SDF出力ではカラーの字形だけ通常の画像にする(出力と同じ)
            bool sdfGlyph = options.SdfOutput && !(has && renderer.IsColorGlyph(g));
            int sdfPadding = 0;
            using var bmp = sdfGlyph ? renderer.RenderSdf(g, out sdfPadding) : renderer.Render(g);
            float w = bmp?.Width ?? options.FontSize / 2, h = bmp?.Height ?? options.FontSize;

            if (x + w > canvasWidth - gap && x > gap)
            {
                x = gap;
                y += rowH + gap;
                rowH = 0;
            }

            var rect = SKRect.Create(x, y, w, h);
            DrawChecker(canvas, rect);
            if (bmp is not null) { using var img = SKImage.FromBitmap(bmp); canvas.DrawImage(img, x, y, SKSamplingOptions.Default); }
            canvas.DrawRect(rect, frame);
            if (sdfGlyph && bmp is not null)
            {
                // 余白の内側(セル)を点線で示す
                canvas.DrawRect(SKRect.Create(x + sdfPadding, y + sdfPadding, w - sdfPadding * 2, h - sdfPadding * 2), sdfCell);
            }
            if (!has)
            {
                // フォントに無い文字は赤い×印
                missingCount++;
                canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, missing);
                canvas.DrawLine(rect.Right, rect.Top, rect.Left, rect.Bottom, missing);
            }
            else if (skipBlank && renderer.IsBlank(g))
            {
                // 描くと空白になる文字はオレンジの点線枠
                blankCount++;
                canvas.DrawRect(rect, blankMark);
            }

            x += w + gap;
            rowH = Math.Max(rowH, h);
        }

        int cellH = renderer.CellHeight;
        PreviewInfo.Text = $"フォント: {typeface.FamilyName}　セル高さ: {cellH}px"
            + (options.SdfOutput
                ? $"　SDF: 余白 {SdfGenerator.Padding(cellH)}px・spread {SdfGenerator.Spread(cellH):0.##}px・倍率 {options.SdfSupersample}"
                  + "　水色の点線 = セル（余白の内側）"
                : "")
            + "　青枠 = 出力される画像の範囲"
            + (missingCount > 0 ? $"　赤× = フォントに無い文字 ({missingCount})" : "")
            + (blankCount > 0 ? $"　橙の点線 = 空白のため出力しない文字 ({blankCount})" : "")
            + (FontLoader.GetColorFontWarning(typeface) is { } warn ? "\n⚠ " + warn : "");
    }

    /// <summary>透明部分がわかるように市松模様を描く</summary>
    private static void DrawChecker(SKCanvas canvas, SKRect rect)
    {
        const int cell = 8;
        using var light = new SKPaint { Color = new SKColor(0x70, 0x70, 0x70) };
        using var dark = new SKPaint { Color = new SKColor(0x58, 0x58, 0x58) };
        canvas.Save();
        canvas.ClipRect(rect);
        for (float yy = rect.Top; yy < rect.Bottom; yy += cell)
            for (float xx = rect.Left; xx < rect.Right; xx += cell)
            {
                bool odd = (((int)((xx - rect.Left) / cell) + (int)((yy - rect.Top) / cell)) & 1) == 1;
                canvas.DrawRect(xx, yy, cell, cell, odd ? dark : light);
            }
        canvas.Restore();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _typeface?.Dispose();
        base.OnClosed(e);
    }
}
