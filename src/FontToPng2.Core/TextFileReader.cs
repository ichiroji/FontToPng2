using System.Text;

namespace FontToPng2.Core;

/// <summary>文字コードを自動判別してテキストファイルを読む</summary>
public static class TextFileReader
{
    static TextFileReader()
    {
        // .NET では Shift_JIS(CP932) を使うのにプロバイダーの登録が必要
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// ファイルを読み、本文と判別した文字コード名を返す。
    /// BOM があればそれに従い、無ければ UTF-8 として正しいか調べ、正しくなければ Shift_JIS とみなす。
    /// </summary>
    public static (string Text, string EncodingName) Read(string path) => Decode(File.ReadAllBytes(path));

    public static (string Text, string EncodingName) Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> b = bytes;
        if (b.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return (Encoding.UTF8.GetString(b[3..]), "UTF-8 (BOM付き)");
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return (Encoding.Unicode.GetString(b[2..]), "UTF-16 LE");
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return (Encoding.BigEndianUnicode.GetString(b[2..]), "UTF-16 BE");

        try
        {
            // 不正なバイト列で例外を出す UTF-8 デコーダーで試す
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return (strictUtf8.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(932).GetString(bytes), "Shift_JIS");
        }
    }
}
