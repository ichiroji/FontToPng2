namespace FontToPng2.Core;

/// <summary>
/// SDF(Signed Distance Field)の距離値を作る。
/// Yuka2 の SdfBuildFromCoverage()(Yuka2/src/cSdf.cpp)の1対1の移植で、同じ入力から同じ値を出す。
/// エンジンは SDF済み画像の値をそのまま使い、太さ・縁取りのしきい値を SpreadRatio=0.25 前提で計算するので、
/// アルゴリズム・定数・丸めを変えないこと(変えるとエンジンの実行時SDFと太さ・縁取りの幅が食い違う)。
///
/// 値の意味: 内側が正の符号付き距離 d(出力の画素単位)を t = 0.5 + d / (2×spread) で 0～1 にし、
///           (byte)(t×255 + 0.5) で格納する(境界=128)。
/// 仕様書: Yuka2/Document/SDF済み画像フォント仕様.md
/// </summary>
public static class SdfGenerator
{
    /// <summary>文字高に対する spread の比率(Yuka2 の SDF_SPREAD_RATIO)。変えてはいけない</summary>
    public const float SpreadRatio = 0.25f;

    /// <summary>距離変換で「特徴点が無い」ことを表す大きな値</summary>
    private const float Inf = 1e20f;

    /// <summary>文字高(余白を除いたセルの高さ。出力の画素)から spread を求める</summary>
    public static float Spread(float fontHeight) => fontHeight * SpreadRatio;

    /// <summary>文字高から上下左右に付ける余白(出力の画素)を求める。ceil(spread)</summary>
    public static int Padding(float fontHeight) => (int)MathF.Ceiling(Spread(fontHeight));

    /// <summary>
    /// 8bitカバレッジから距離値を作る(SdfBuildFromCoverage の移植)。
    /// </summary>
    /// <param name="coverage">入力の8bitカバレッジ(0=外側 255=内側。高解像度)。余白込みで渡すこと</param>
    /// <param name="width">入力の幅</param>
    /// <param name="height">入力の高さ</param>
    /// <param name="pitch">入力の1行のバイト数</param>
    /// <param name="scale">縮小倍率(入力の scale×scale 画素を出力の1画素にする)</param>
    /// <param name="spread">距離を記録する幅(出力の画素単位)</param>
    /// <param name="outWidth">出力の幅(= width / scale。端数は捨てる)</param>
    /// <param name="outHeight">出力の高さ(= height / scale)</param>
    /// <returns>距離値(outWidth×outHeight、詰めて格納)。引数が不正なら null</returns>
    public static byte[]? BuildFromCoverage(ReadOnlySpan<byte> coverage, int width, int height, int pitch,
                                            int scale, float spread, out int outWidth, out int outHeight)
    {
        outWidth = 0;
        outHeight = 0;
        if (width <= 0 || height <= 0 || scale <= 0 || spread <= 0.0f) return null;
        if (coverage.Length < pitch * (height - 1) + width) return null;

        int ow = width / scale;
        int oh = height / scale;
        if (ow <= 0 || oh <= 0) return null;

        // 内側までの距離(外側の画素で意味を持つ)と外側までの距離(内側の画素で意味を持つ)
        int count = width * height;
        var toInside = new float[count];
        var toOutside = new float[count];
        for (int y = 0; y < height; y++)
        {
            int src = pitch * y;
            for (int x = 0; x < width; x++)
            {
                bool inside = coverage[src + x] >= 128;
                toInside[y * width + x] = inside ? 0.0f : Inf;
                toOutside[y * width + x] = inside ? Inf : 0.0f;
            }
        }
        Distance2D(toInside, width, height);
        Distance2D(toOutside, width, height);

        // 符号付き距離を scale×scale で平均し、出力の画素単位で 0～255 へ詰める
        // (画素の中心どうしの距離なので、境界は隣り合う画素の中間=0.5画素ずらす)
        // 縁の画素(カバレッジが 0 と 255 の間)は、2値化で失った境界の位置をカバレッジから補う
        var output = new byte[ow * oh];
        float blockArea = (float)(scale * scale);
        for (int oy = 0; oy < oh; oy++)
        {
            for (int ox = 0; ox < ow; ox++)
            {
                float sum = 0.0f;
                for (int by = 0; by < scale; by++)
                {
                    int hy = oy * scale + by;
                    int baseIndex = hy * width + ox * scale;
                    int src = pitch * hy + ox * scale;
                    for (int bx = 0; bx < scale; bx++)
                    {
                        byte c = coverage[src + bx];
                        float sd;
                        if (c > 0 && c < 255)
                        {
                            sd = (float)c / 255.0f - 0.5f;
                        }
                        else
                        {
                            float dout = toOutside[baseIndex + bx];
                            float din = toInside[baseIndex + bx];
                            sd = (din == 0.0f) ? (MathF.Sqrt(dout) - 0.5f) : -(MathF.Sqrt(din) - 0.5f);
                        }
                        sum += sd;
                    }
                }
                float d = (sum / blockArea) / (float)scale; // 出力の画素単位
                float t = 0.5f + d / (2.0f * spread);
                if (t < 0.0f) t = 0.0f;
                if (t > 1.0f) t = 1.0f;
                output[oy * ow + ox] = (byte)(t * 255.0f + 0.5f);
            }
        }

        outWidth = ow;
        outHeight = oh;
        return output;
    }

    /// <summary>1次元の距離変換を縦→横の順に掛けて、2次元のユークリッド距離の二乗を求める</summary>
    private static void Distance2D(float[] grid, int width, int height)
    {
        int n = Math.Max(width, height);
        var f = new float[n];
        var d = new float[n];
        var v = new int[n];
        var z = new float[n + 1];

        // 縦方向
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++) f[y] = grid[y * width + x];
            Distance1D(f, height, d, v, z);
            for (int y = 0; y < height; y++) grid[y * width + x] = d[y];
        }
        // 横方向
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++) f[x] = grid[row + x];
            Distance1D(f, width, d, v, z);
            for (int x = 0; x < width; x++) grid[row + x] = d[x];
        }
    }

    /// <summary>Felzenszwalb の1次元二乗距離変換(下側包絡線を求める方式。O(n))</summary>
    private static void Distance1D(float[] f, int n, float[] d, int[] v, float[] z)
    {
        int k = 0;
        v[0] = 0;
        z[0] = -Inf;
        z[1] = +Inf;
        for (int q = 1; q < n; q++)
        {
            float s = ((f[q] + (float)q * q) - (f[v[k]] + (float)v[k] * v[k])) / (float)(2 * q - 2 * v[k]);
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + (float)q * q) - (f[v[k]] + (float)v[k] * v[k])) / (float)(2 * q - 2 * v[k]);
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = +Inf;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < (float)q) k++;
            float dq = (float)(q - v[k]);
            d[q] = dq * dq + f[v[k]];
        }
    }
}
