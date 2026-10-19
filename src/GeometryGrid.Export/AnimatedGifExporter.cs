using System.IO;
using System.Windows.Media.Imaging;
using GeometryGrid.Core.Rendering;

namespace GeometryGrid.Export;

public sealed record AnimatedGifFrame(
    RenderScene Scene,
    int DelayCentiseconds,
    double? PreciseDurationSeconds = null);

public static class AnimatedGifExporter
{
    public static void Export(
        IReadOnlyList<AnimatedGifFrame> frames,
        string path,
        int width,
        int height,
        double dpi = 96)
    {
        if (frames.Count == 0)
            throw new ArgumentException("GIF 至少需要一帧。", nameof(frames));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "GIF 尺寸必须大于 0。");

        var encoder = new GifBitmapEncoder();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var bitmap = RasterExporter.Render(
                frame.Scene,
                width,
                height,
                dpi,
                transparent: false);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery(
                "/grctlext/Delay",
                (ushort)System.Math.Clamp(frame.DelayCentiseconds, 1, ushort.MaxValue));
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            if (index == 0)
            {
                // NETSCAPE2.0 application extension: repeat forever.
                metadata.SetQuery(
                    "/appext/application",
                    "NETSCAPE2.0"u8.ToArray());
                metadata.SetQuery(
                    "/appext/data",
                    new byte[] { 3, 1, 0, 0 });
            }
            encoder.Frames.Add(BitmapFrame.Create(
                bitmap,
                null,
                metadata,
                null));
        }

        using var encoded = new MemoryStream();
        encoder.Save(encoded);
        // WIC can silently discard BitmapFrame GIF metadata. Write the actual GIF control
        // blocks after encoding, traversing blocks rather than searching compressed pixels.
        File.WriteAllBytes(path, ApplyAnimationMetadata(encoded.ToArray(), frames));
    }

    private static byte[] ApplyAnimationMetadata(byte[] source, IReadOnlyList<AnimatedGifFrame> frames)
    {
        if (source.Length < 13) throw new InvalidDataException("GIF 编码结果不完整。");
        var offset = 13 + ((source[10] & 0x80) != 0 ? 3 * (1 << ((source[10] & 7) + 1)) : 0);
        using var output = new MemoryStream();
        output.Write(source, 0, offset);
        // Loop indefinitely, with one unambiguous application extension.
        output.Write(new byte[] { 0x21, 0xFF, 11 });
        output.Write("NETSCAPE2.0"u8);
        output.Write(new byte[] { 3, 1, 0, 0, 0 });
        var frameIndex = 0;
        void SkipSubBlocks()
        {
            while (offset < source.Length)
            {
                var size = source[offset++];
                if (size == 0) return;
                offset = checked(offset + size);
                if (offset > source.Length) throw new InvalidDataException("GIF 数据块不完整。");
            }
            throw new InvalidDataException("GIF 数据块缺少终止符。");
        }
        while (offset < source.Length)
        {
            var start = offset;
            var marker = source[offset++];
            if (marker == 0x21)
            {
                var label = source[offset++];
                var existingLoop = label == 0xFF && offset + 12 <= source.Length &&
                                   source.AsSpan(offset + 1, 11).SequenceEqual("NETSCAPE2.0"u8);
                SkipSubBlocks();
                if (label != 0xF9 && !existingLoop) output.Write(source, start, offset - start);
            }
            else if (marker == 0x2C)
            {
                if (frameIndex >= frames.Count || offset + 9 > source.Length)
                    throw new InvalidDataException("GIF 帧数不匹配。");
                var flags = source[offset + 8];
                offset += 9 + ((flags & 0x80) != 0 ? 3 * (1 << ((flags & 7) + 1)) : 0);
                offset++; // LZW minimum code size.
                SkipSubBlocks();
                var delay = Math.Clamp(frames[frameIndex++].DelayCentiseconds, 1, ushort.MaxValue);
                output.Write(new byte[] { 0x21, 0xF9, 4, 8, (byte)delay, (byte)(delay >> 8), 0, 0 });
                output.Write(source, start, offset - start);
            }
            else if (marker == 0x3B)
            {
                output.WriteByte(marker);
                if (frameIndex != frames.Count) throw new InvalidDataException("GIF 帧数不匹配。");
                return output.ToArray();
            }
            else throw new InvalidDataException("GIF 数据块类型无效。");
        }
        throw new InvalidDataException("GIF 编码结果缺少文件终止符。");
    }
}
