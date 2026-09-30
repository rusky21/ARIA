using FluentAssertions;
using Xunit;

namespace AI_Supporter.Tests;

public class VoicePipelineTests
{
    private static byte[] BuildWav(float[] samples, int sampleRate, int channels, int bits)
    {
        var bytesPerSample = bits / 8;
        var dataLen = samples.Length * bytesPerSample;
        var buffer = new byte[44 + dataLen];
        BitConverter.GetBytes(0x46464952).CopyTo(buffer, 0);
        BitConverter.GetBytes(36 + dataLen).CopyTo(buffer, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(buffer, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(buffer, 12);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(buffer, 36);
        BitConverter.GetBytes(16).CopyTo(buffer, 16);
        BitConverter.GetBytes((short)1).CopyTo(buffer, 20);
        BitConverter.GetBytes((short)channels).CopyTo(buffer, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(buffer, 24);
        BitConverter.GetBytes(sampleRate * channels * bytesPerSample).CopyTo(buffer, 28);
        BitConverter.GetBytes((short)(channels * bytesPerSample)).CopyTo(buffer, 32);
        BitConverter.GetBytes((short)bits).CopyTo(buffer, 34);
        BitConverter.GetBytes(dataLen).CopyTo(buffer, 40);

        for (var i = 0; i < samples.Length; i++)
        {
            var s = Math.Clamp(samples[i], -1f, 1f);
            if (bits == 16)
                BitConverter.GetBytes((short)(s < 0 ? s * 0x8000 : s * 0x7FFF)).CopyTo(buffer, 44 + i * 2);
            else if (bits == 8)
                buffer[44 + i] = (byte)((s + 1f) * 127f);
        }
        return buffer;
    }

    private static float[] BuildSine(double freq, double seconds, int sampleRate)
    {
        var count = (int)(seconds * sampleRate);
        var result = new float[count];
        for (var i = 0; i < count; i++)
            result[i] = (float)(0.5 * Math.Sin(2 * Math.PI * freq * i / sampleRate));
        return result;
    }

    [Fact]
    public void DecodeWav16_Resamples44100To16000()
    {
        var samples = BuildSine(440, 1.0, 44100);
        var wav = BuildWav(samples, 44100, 1, 16);

        var decoded = WakeDsp.DecodeWav16(wav);

        decoded.Length.Should().BeCloseTo(WakeDsp.SampleRate, 200);
    }

    [Fact]
    public void DecodeWav16_MixesStereoToMono()
    {
        var samples = new float[44100];
        for (var i = 0; i < samples.Length; i += 2)
        {
            samples[i] = 0.5f;
            samples[i + 1] = -0.5f;
        }
        var wav = BuildWav(samples, 44100, 2, 16);

        var decoded = WakeDsp.DecodeWav16(wav);

        decoded.Length.Should().Be(8000);
    }

    [Fact]
    public void DecodeWav16_16kMono16bit_Unchanged()
    {
        var samples = BuildSine(440, 0.5, 16000);
        var wav = BuildWav(samples, 16000, 1, 16);

        var decoded = WakeDsp.DecodeWav16(wav);

        decoded.Length.Should().Be(samples.Length);
    }

    [Fact]
    public void NormalizeCmvn_MakesZeroMeanUnitVariance()
    {
        var mfcc = new float[10][];
        for (var i = 0; i < 10; i++)
        {
            mfcc[i] = new float[13];
            for (var d = 0; d < 13; d++)
                mfcc[i][d] = i * 3.7f + d;
        }

        WakeDsp.NormalizeCmvn(mfcc);

        for (var d = 0; d < 13; d++)
        {
            var mean = 0.0;
            for (var i = 0; i < 10; i++) mean += mfcc[i][d];
            mean /= 10;
            mean.Should().BeApproximately(0, 1e-4);
        }
    }

    [Fact]
    public void NormalizeCmvn_InvariantToAmplitudeScale()
    {
        var samples = BuildSine(300, 0.5, WakeDsp.SampleRate);
        var scaled = samples.Select(s => s * 0.3f).ToArray();

        var mfcc1 = WakeDsp.NormalizeCmvn(WakeDsp.ComputeMfcc(samples));
        var mfcc2 = WakeDsp.NormalizeCmvn(WakeDsp.ComputeMfcc(scaled));

        var diff = 0.0;
        for (var i = 0; i < Math.Min(mfcc1.Length, mfcc2.Length); i++)
            for (var d = 0; d < 13; d++)
                diff += Math.Abs(mfcc1[i][d] - mfcc2[i][d]);
        diff.Should().BeLessThan(1.0);
    }

    [Fact]
    public void ExtractSpeech_KeepsContinuousSpeechSegment()
    {
        var speech = BuildSine(300, 1.0, WakeDsp.SampleRate);
        var silence = new float[WakeDsp.SampleRate / 2];
        var samples = silence.Concat(speech).Concat(silence).ToArray();

        var result = WakeDsp.ExtractSpeech(samples);

        result.Length.Should().BeGreaterThan((int)(WakeDsp.SampleRate * 0.8));
    }

    [Fact]
    public void ExtractSpeech_ReturnsEmpty_ForSilence()
    {
        var samples = new float[WakeDsp.SampleRate];

        var result = WakeDsp.ExtractSpeech(samples);

        result.Length.Should().Be(0);
    }

    [Theory]
    [InlineData("Спасибо за просмотр!", null)]
    [InlineData("Субтитры сделал(а) DimaTorzok", null)]
    [InlineData("Включи свет", "Включи свет")]
    [InlineData("  ", null)]
    public void CleanTranscription_FiltersHallucinations(string input, string? expected)
    {
        var result = WhisperService.CleanTranscription(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void WakeWordService_TrainAndCheck_SameAudio_Hits()
    {
        var settings = new WakeSettings { Enabled = true, Threshold = 0.30f };
        var svc = new WakeWordService(settings);

        var samples = BuildSine(300, 1.0, WakeDsp.SampleRate);
        var wav = BuildWav(samples, WakeDsp.SampleRate, 1, 16);

        var train = svc.Train(wav);
        train.Ok.Should().BeTrue();

        var check = svc.Check(wav);
        check.IsHotword.Should().BeTrue();
    }
}
