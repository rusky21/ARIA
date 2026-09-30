using System.Text;

namespace AI_Supporter;

/// <summary>
/// Чистый C# DSP для не-нейросетевого wake-детектора (без NuGet, без Whisper/LLM):
/// WAV (16 кГц, 16 бит, моно) → VAD по энергии → MFCC (свой FFT Cooley-Tukey + DCT-II) → DTW.
/// </summary>
public static class WakeDsp
{
    public const int SampleRate = 16000;
    private const int FrameSize = 400;      // 25 мс
    public const int FrameHop = 160;        // 10 мс
    private const int FftSize = 512;        // >= FrameSize, степень двойки
    private const int NumMel = 24;
    private const int NumCep = 13;          // коэффициенты 1..13 (без c0 — энергетика)
    private const double PreEmphasis = 0.97;
    private const double MelLo = 300.0;
    private const double MelHi = 8000.0;

    // ── WAV ────────────────────────────────────────────────────────────────
    public static float[] DecodeWav16(byte[] wav)
    {
        if (wav is null || wav.Length < 44) return Array.Empty<float>();
        var pos = 12;
        int channels = 1, sampleRate = SampleRate, bits = 16;
        while (pos + 8 <= wav.Length)
        {
            var id = Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            if (id == "fmt " && pos + 24 <= wav.Length)
            {
                channels = BitConverter.ToInt16(wav, pos + 10);
                sampleRate = BitConverter.ToInt32(wav, pos + 12);
                bits = BitConverter.ToInt16(wav, pos + 22);
            }
            else if (id == "data")
            {
                var dataStart = pos + 8;
                var dataLen = Math.Min(size, wav.Length - dataStart);
                var samples = DecodeSamples(wav, dataStart, dataLen, bits);
                return NormalizeTo16kMono(samples, channels, sampleRate);
            }
            pos += 8 + size + (size & 1);
        }
        return DecodeSamples(wav, 44, wav.Length - 44, 16);
    }

    private static float[] DecodeSamples(byte[] wav, int start, int length, int bits)
    {
        if (bits == 16)
        {
            var count = length / 2;
            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                var raw = BitConverter.ToInt16(wav, start + i * 2);
                result[i] = raw / 32768f;
            }
            return result;
        }
        if (bits == 32)
        {
            var count = length / 4;
            var result = new float[count];
            for (var i = 0; i < count; i++)
                result[i] = BitConverter.ToSingle(wav, start + i * 4);
            return result;
        }
        if (bits == 8)
        {
            var count = length;
            var result = new float[count];
            for (var i = 0; i < count; i++)
                result[i] = (wav[start + i] - 128) / 128f;
            return result;
        }
        return Array.Empty<float>();
    }

    public static float[] NormalizeTo16kMono(float[] samples, int channels, int sampleRate)
    {
        if (samples.Length == 0) return samples;

        var mono = samples;
        if (channels > 1)
        {
            var frames = samples.Length / channels;
            mono = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++) sum += samples[i * channels + c];
                mono[i] = sum / channels;
            }
        }

        if (sampleRate == SampleRate || mono.Length == 0) return mono;
        if (sampleRate <= 0) return mono;

        var ratio = (double)sampleRate / SampleRate;
        var outLen = (int)(mono.Length / ratio);
        var result = new float[outLen];
        for (var i = 0; i < outLen; i++)
        {
            var srcPos = i * ratio;
            var i0 = (int)srcPos;
            var frac = srcPos - i0;
            var s0 = mono[i0];
            var s1 = i0 + 1 < mono.Length ? mono[i0 + 1] : s0;
            result[i] = s0 + (s1 - s0) * (float)frac;
        }
        return result;
    }

    // ── VAD: вырезаем самый длинный речевой сегмент по энергии кадров ──────
    public static float[] ExtractSpeech(float[] samples, float vadFloor = 0.015f)
    {
        var n = samples.Length;
        if (n < FrameSize) return Array.Empty<float>();
        var numFrames = (n - FrameSize) / FrameHop + 1;
        var rms = new float[numFrames];
        for (var f = 0; f < numFrames; f++)
        {
            var sum = 0.0;
            var off = f * FrameHop;
            for (var i = 0; i < FrameSize; i++)
            {
                var s = samples[off + i];
                sum += s * s;
            }
            rms[f] = (float)Math.Sqrt(sum / FrameSize);
        }

        var maxRms = 0f;
        foreach (var r in rms)
            if (r > maxRms) maxRms = r;
        var thr = Math.Max(vadFloor, maxRms * 0.1f);

        var bestStart = -1;
        var bestLen = 0;
        var curStart = -1;
        for (var f = 0; f < numFrames; f++)
        {
            if (rms[f] > thr)
            {
                if (curStart < 0) curStart = f;
                var len = f - curStart + 1;
                if (len > bestLen)
                {
                    bestLen = len;
                    bestStart = curStart;
                }
            }
            else if (curStart >= 0 && f - curStart > 8)
            {
                curStart = -1;
            }
        }
        if (bestStart < 0) return Array.Empty<float>();

        var first = Math.Max(0, bestStart - 5);
        var last = Math.Min(numFrames - 1, bestStart + bestLen - 1 + 5);
        var start = first * FrameHop;
        var end = Math.Min(n, last * FrameHop + FrameSize);
        var seg = new float[end - start];
        Array.Copy(samples, start, seg, 0, end - start);
        return seg;
    }

    // ── CMVN: нормализация среднее/дисперсия по каждому коэффициенту ───────
    public static float[][] NormalizeCmvn(float[][] mfcc)
    {
        if (mfcc.Length == 0 || mfcc[0].Length == 0) return mfcc;
        var dim = mfcc[0].Length;
        var mean = new double[dim];
        var varSum = new double[dim];

        foreach (var frame in mfcc)
            for (var d = 0; d < dim; d++)
                mean[d] += frame[d];

        for (var d = 0; d < dim; d++) mean[d] /= mfcc.Length;

        foreach (var frame in mfcc)
            for (var d = 0; d < dim; d++)
            {
                var diff = frame[d] - mean[d];
                varSum[d] += diff * diff;
            }

        for (var d = 0; d < dim; d++)
        {
            var std = Math.Sqrt(varSum[d] / mfcc.Length);
            if (std < 1e-6) std = 1e-6;
            var inv = 1.0 / std;
            for (var i = 0; i < mfcc.Length; i++)
                mfcc[i][d] = (float)((mfcc[i][d] - mean[d]) * 2.0 / std);
        }
        return mfcc;
    }

    // ── MFCC ───────────────────────────────────────────────────────────────
    public static float[][] ComputeMfcc(float[] samples)
    {
        var n = samples.Length;
        if (n < FrameSize) return Array.Empty<float[]>();
        var numFrames = (n - FrameSize) / FrameHop + 1;
        if (numFrames > 500) numFrames = 500;

        var hamming = new float[FrameSize];
        for (var i = 0; i < FrameSize; i++)
            hamming[i] = (float)(0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / (FrameSize - 1)));

        var filters = BuildMelFilters();
        var result = new float[numFrames][];
        for (var f = 0; f < numFrames; f++)
        {
            var off = f * FrameHop;
            var window = new float[FftSize];
            var prev = 0f;
            for (var i = 0; i < FrameSize; i++)
            {
                var s = samples[off + i];
                var y = s - (float)(PreEmphasis * prev);
                prev = s;
                window[i] = y * hamming[i];
            }
            result[f] = MelCepstrum(window, filters);
        }
        return result;
    }

    private static float[] MelCepstrum(float[] window, float[][] filters)
    {
        var spectrum = PowerSpectrum(window);
        var melEnergy = new double[NumMel];
        for (var m = 0; m < NumMel; m++)
        {
            var sum = 0.0;
            var f = filters[m];
            for (var k = 0; k < spectrum.Length; k++)
                sum += f[k] * spectrum[k];
            melEnergy[m] = Math.Log(sum + 1e-10);
        }

        var ceps = new float[NumCep];
        for (var c = 0; c < NumCep; c++)
        {
            var sum = 0.0;
            for (var m = 0; m < NumMel; m++)
                sum += melEnergy[m] * Math.Cos(Math.PI * (c + 1) * (2.0 * m + 1) / (2.0 * NumMel));
            ceps[c] = (float)sum;
        }
        return ceps;
    }

    private static float[][] BuildMelFilters()
    {
        var binFreq = (double)SampleRate / FftSize;
        var fftBins = FftSize / 2 + 1;
        var melPoints = new int[NumMel + 2];
        for (var m = 0; m < NumMel + 2; m++)
        {
            var mel = MelScale(MelLo) + m * (MelScale(MelHi) - MelScale(MelLo)) / (NumMel + 1);
            var hz = MelInverse(mel);
            melPoints[m] = (int)Math.Round(hz / binFreq);
        }

        var filters = new float[NumMel][];
        for (var m = 1; m <= NumMel; m++)
        {
            var left = melPoints[m - 1];
            var center = melPoints[m];
            var right = melPoints[m + 1];
            var filter = new float[fftBins];
            for (var k = 0; k < fftBins; k++)
            {
                if (k < left || k > right) continue;
                filter[k] = k < center
                    ? (float)((k - left) / (double)Math.Max(1, center - left))
                    : (float)((right - k) / (double)Math.Max(1, right - center));
            }
            filters[m - 1] = filter;
        }
        return filters;
    }

    private static double MelScale(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);
    private static double MelInverse(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

    private static double[] PowerSpectrum(float[] window)
    {
        var real = new double[FftSize];
        var imag = new double[FftSize];
        for (var i = 0; i < FftSize; i++) real[i] = window[i];
        Fft(real, imag, false);

        var bins = FftSize / 2 + 1;
        var power = new double[bins];
        for (var k = 0; k < bins; k++)
            power[k] = real[k] * real[k] + imag[k] * imag[k];
        return power;
    }

    // Cooley-Tukey (radix-2, iterative), FftSize — степень двойки.
    private static void Fft(double[] re, double[] im, bool inverse)
    {
        var n = re.Length;
        for (var i = 1; i < n; i++)
        {
            var j = ReverseBits(i, n);
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = 2.0 * Math.PI / len * (inverse ? 1.0 : -1.0);
            var wRe = Math.Cos(ang);
            var wIm = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                var curRe = 1.0;
                var curIm = 0.0;
                for (var k = 0; k < len / 2; k++)
                {
                    var uRe = re[i + k];
                    var uIm = im[i + k];
                    var vRe = re[i + k + len / 2] * curRe - im[i + k + len / 2] * curIm;
                    var vIm = re[i + k + len / 2] * curIm + im[i + k + len / 2] * curRe;
                    re[i + k] = uRe + vRe;
                    im[i + k] = uIm + vIm;
                    re[i + k + len / 2] = uRe - vRe;
                    im[i + k + len / 2] = uIm - vIm;
                    var nRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nRe;
                }
            }
        }

        if (inverse)
        {
            for (var i = 0; i < n; i++)
            {
                re[i] /= n;
                im[i] /= n;
            }
        }
    }

    private static int ReverseBits(int value, int n)
    {
        var bits = 0;
        var j = value;
        while (n > 1)
        {
            bits = (bits << 1) | (j & 1);
            j >>= 1;
            n >>= 1;
        }
        return bits;
    }

    // ── DTW ────────────────────────────────────────────────────────────────
    public static float DtwDistance(float[][] a, float[][] b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0 || m == 0) return float.MaxValue;

        var prev = new double[m];
        var cur = new double[m];
        for (var j = 0; j < m; j++) prev[j] = double.MaxValue;

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                var d = VecDist(a[i], b[j]);
                var min = double.MaxValue;
                if (i == 0 && j == 0) min = 0.0;
                else
                {
                    if (i > 0 && prev[j] < min) min = prev[j];
                    if (j > 0 && cur[j - 1] < min) min = cur[j - 1];
                    if (i > 0 && j > 0 && prev[j - 1] < min) min = prev[j - 1];
                }
                cur[j] = d + min;
            }
            (prev, cur) = (cur, prev);
            for (var j = 0; j < m; j++) cur[j] = double.MaxValue;
        }
        return (float)prev[m - 1];
    }

    private static double VecDist(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }
}
