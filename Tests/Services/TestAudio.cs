using System;
using System.IO;

namespace DPlayer.Tests.Services;

/// <summary>
/// 测试用 WAV 生成器：不依赖 %TEMP% 下的外部样本文件，也不受运行机器上音频文件有无的影响。
/// amplitude 默认为 0（静音）—— 需要真正播放的用例应显式传静音，避免测试运行时发声。
/// </summary>
internal static class TestAudio
{
    /// <summary>写一个 44.1kHz 单声道 16bit 正弦 WAV。</summary>
    public static void WriteWav(string path, int seconds, short amplitude = 4000)
    {
        const int sampleRate = 44100, channels = 1, bits = 16;
        int samples = sampleRate * seconds * channels;
        var bytes = new byte[44 + samples * 2];

        void I32(int v, int o) => BitConverter.GetBytes(v).CopyTo(bytes, o);
        void I16(short v, int o) => BitConverter.GetBytes(v).CopyTo(bytes, o);

        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        I32(36 + samples * 2, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        I32(16, 16); I16(1, 20); I16(channels, 22);
        I32(sampleRate, 24); I32(sampleRate * channels * bits / 8, 28);
        I16((short)(channels * bits / 8), 32); I16(bits, 34);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        I32(samples * 2, 40);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / sampleRate;
            I16((short)Math.Round(amplitude * Math.Sin(2 * Math.PI * 440 * t)), 44 + i * 2);
        }

        File.WriteAllBytes(path, bytes);
    }
}
