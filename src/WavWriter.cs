using System;
using System.IO;

static class WavWriter
{
    // Writes interleaved 16-bit PCM samples (frames * channels values) as a canonical WAV file.
    public static void WritePcm16(string path, short[] samples, int count, int channels, int rate)
    {
        int dataLen = count * 2;
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataLen);
            byte[] buf = new byte[Math.Min(count, 1 << 16) * 2];
            int i = 0;
            while (i < count)
            {
                int n = Math.Min(count - i, buf.Length / 2);
                Buffer.BlockCopy(samples, i * 2, buf, 0, n * 2);
                w.Write(buf, 0, n * 2);
                i += n;
            }
        }
    }
}
