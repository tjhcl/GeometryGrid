using System.IO;
using System.Runtime.InteropServices;

namespace GeometryGrid.Export;

/// <summary>Offline MP4/H.264 export using Windows Media Foundation; no external executable.</summary>
public static class AnimatedVideoExporter
{
    /// <summary>Standard landscape/portrait HD or UHD envelope; never expands a square to 3840×3840.</summary>
    public static (int Width, int Height) GetCompatibleFrameSize(int longEdge, double contentAspectRatio)
    {
        if (longEdge is not (1920 or 3840)) throw new ArgumentOutOfRangeException(nameof(longEdge));
        if (!double.IsFinite(contentAspectRatio) || contentAspectRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(contentAspectRatio));
        var shortEdge = longEdge == 3840 ? 2160 : 1080;
        return contentAspectRatio < 1 ? (shortEdge, longEdge) : (longEdge, shortEdge);
    }

    public static void Export(IReadOnlyList<AnimatedGifFrame> frames, string path,
        int width, int height, int framesPerSecond = 30,
        Action<int, int>? progress = null, CancellationToken cancellationToken = default,
        double? contentAspectRatio = null)
    {
        if (frames.Count == 0) throw new ArgumentException("视频至少需要一帧。", nameof(frames));
        if (width < 2 || height < 2 || width > 3840 || height > 3840 || width % 2 != 0 || height % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(width), "视频宽高必须为 2–3840 的偶数。");
        if (framesPerSecond is not (30 or 60))
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "视频支持 30 或 60 帧/秒。");
        if (!Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("视频文件扩展名必须为 .mp4。", nameof(path));
        if (frames.Any(f => f.PreciseDurationSeconds is { } duration &&
                            (!double.IsFinite(duration) || duration <= 0)))
            throw new ArgumentException("视频帧时长必须为有限正数。", nameof(frames));
        if (contentAspectRatio is { } ratio && (!double.IsFinite(ratio) || ratio <= 0))
            throw new ArgumentOutOfRangeException(nameof(contentAspectRatio));

        var destination = Path.GetFullPath(path);
        // Finalize first, then atomically replace: a failed/cancelled export never destroys an existing video.
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".geometrygrid-{Guid.NewGuid():N}.mp4");
        try
        {
            using (var writer = new MediaFoundationVideoWriter(temporary, width, height, framesPerSecond))
            {
                var pixels = new byte[checked(width * height * 4)];
                double totalSeconds = 0;
                long written = 0;
                for (var index = 0; index < frames.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalSeconds += frames[index].PreciseDurationSeconds ?? Math.Max(1, frames[index].DelayCentiseconds) / 100d;
                    var expected = (long)Math.Round(totalSeconds * framesPerSecond,
                        MidpointRounding.AwayFromZero);
                    if (expected > written)
                    {
                        var bitmap = contentAspectRatio is { } aspect
                            ? RasterExporter.RenderVideoFrame(frames[index].Scene, width, height, aspect)
                            : RasterExporter.Render(frames[index].Scene, width, height, 96, false);
                        bitmap.CopyPixels(pixels, width * 4, 0);
                        // Opaque Pbgra32 has the same byte layout as Media Foundation RGB32.
                        while (written < expected)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            writer.Write(pixels, written++);
                        }
                    }
                    progress?.Invoke(index + 1, frames.Count);
                }
                writer.Complete();
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

// Vtable positions follow the Windows SDK IMFAttributes, IMFSample, IMFMediaBuffer and
// IMFSinkWriter definitions. All native pointers are scoped and released on every error path.
internal sealed class MediaFoundationVideoWriter : IDisposable
{
    private IntPtr _writer;
    private readonly int _fps;
    private int _stream;
    private bool _started;
    private bool _comInitialized;

    internal MediaFoundationVideoWriter(string path, int width, int height, int fps)
    {
        _fps = fps;
        var stage = "初始化 COM / 媒体组件";
        try
        {
            var com = CoInitializeEx(IntPtr.Zero, 0); // MTA worker; an already initialized STA is also valid.
            if (com >= 0) _comInitialized = true;
            else if (com != unchecked((int)0x80010106)) Check(com);
            Check(MFStartup(0x20070, 0));
            _started = true;
            Check(MFCreateAttributes(out var attributes, 1));
            try
            {
                Set32(attributes, "08b845d8-2b74-4afe-9d53-be16d2d5ae4f", 1); // Disable realtime throttling.
                stage = "创建 MP4 输出文件";
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out _writer));
            }
            finally { Release(attributes); }

            Check(MFCreateMediaType(out var output));
            try
            {
                Configure(output, "34363248-0000-0010-8000-00aa00389b71", width, height, fps); // H264
                Set32(output, "20332624-fb0d-4d9e-bd0d-cbf6786c102e",
                    (uint)Math.Clamp((long)width * height * fps / 6, 6_000_000L, 40_000_000L));
                stage = "配置 H.264 输出格式";
                Check(Call<AddStream>(_writer, 3)(_writer, output, out _stream));
            }
            finally { Release(output); }
            Check(MFCreateMediaType(out var input));
            try
            {
                Configure(input, "00000016-0000-0010-8000-00aa00389b71", width, height, fps); // RGB32
                Set32(input, "644b4e48-1e02-4516-b0eb-c01ca9d49ac6", (uint)(width * 4)); // top-down
                stage = "协商像素格式与编码尺寸";
                Check(Call<SetInput>(_writer, 4)(_writer, _stream, input, IntPtr.Zero));
            }
            finally { Release(input); }
            stage = "启动编码";
            Check(Call<NoArgs>(_writer, 5)(_writer)); // BeginWriting
        }
        catch (Exception ex)
        {
            Dispose();
            var hint = ex.HResult == unchecked((int)0xC00D36B4)
                ? "系统编码器不支持此次尺寸或像素格式；请使用标准高清/4K画幅。"
                : ex is DllNotFoundException ? "系统媒体组件不可用，请检查 Windows 媒体功能。"
                : "请检查输出目录是否可写，以及系统媒体编码组件是否可用。";
            throw new InvalidOperationException($"MP4 导出初始化失败：{stage}。\n{width}×{height}，{fps} 帧/秒；错误码 0x{ex.HResult:X8}。\n{hint}\n原始错误：{ex.Message}", ex);
        }
    }

    internal void Write(byte[] pixels, long frameIndex)
    {
        Check(MFCreateMemoryBuffer(pixels.Length, out var buffer));
        IntPtr sample = IntPtr.Zero;
        try
        {
            Check(Call<LockBuffer>(buffer, 3)(buffer, out var data, out _, out _));
            try { Marshal.Copy(pixels, 0, data, pixels.Length); }
            finally { Check(Call<NoArgs>(buffer, 4)(buffer)); }
            Check(Call<SetLength>(buffer, 6)(buffer, pixels.Length));
            Check(MFCreateSample(out sample));
            Check(Call<PointerArg>(sample, 42)(sample, buffer)); // AddBuffer
            var time = frameIndex * 10_000_000 / _fps;
            var next = (frameIndex + 1) * 10_000_000 / _fps;
            Check(Call<LongArg>(sample, 36)(sample, time)); // SetSampleTime
            Check(Call<LongArg>(sample, 38)(sample, next - time)); // SetSampleDuration
            Check(Call<WriteSample>(_writer, 6)(_writer, _stream, sample));
        }
        finally { Release(sample); Release(buffer); }
    }

    internal void Complete() => Check(Call<NoArgs>(_writer, 11)(_writer)); // Finalize

    public void Dispose()
    {
        Release(_writer); _writer = IntPtr.Zero;
        if (_started) { MFShutdown(); _started = false; }
        if (_comInitialized) { CoUninitialize(); _comInitialized = false; }
    }

    private static void Configure(IntPtr type, string subtype, int width, int height, int fps)
    {
        SetGuid(type, "48eba18e-f8c9-4687-bf11-0a74c9f96a8f", "73646976-0000-0010-8000-00aa00389b71");
        SetGuid(type, "f7e34c9a-42e8-4714-b74b-cb29d72c35e5", subtype);
        Set32(type, "e2724bb8-e676-4806-b4b2-a8d6efb44ccd", 2); // Progressive
        Set64(type, "1652c33d-d6b2-4012-b834-72030849a37d", ((ulong)width << 32) | (uint)height);
        Set64(type, "c459a2e8-3d2c-4e44-b132-fee5156c7bb0", ((ulong)fps << 32) | 1);
        Set64(type, "c6376a1e-8d0a-4027-be45-6d9a0ad39bb6", (1UL << 32) | 1);
    }
    private static void Set32(IntPtr p, string key, uint value) { var id = new Guid(key); Check(Call<UIntAttribute>(p, 21)(p, ref id, value)); }
    private static void Set64(IntPtr p, string key, ulong value) { var id = new Guid(key); Check(Call<ULongAttribute>(p, 22)(p, ref id, value)); }
    private static void SetGuid(IntPtr p, string key, string value) { var id = new Guid(key); var v = new Guid(value); Check(Call<GuidAttribute>(p, 24)(p, ref id, ref v)); }
    private static T Call<T>(IntPtr p, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), slot * IntPtr.Size));
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgs(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UIntAttribute(IntPtr p, ref Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ULongAttribute(IntPtr p, ref Guid key, ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GuidAttribute(IntPtr p, ref Guid key, ref Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddStream(IntPtr p, IntPtr type, out int stream);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetInput(IntPtr p, int stream, IntPtr type, IntPtr attrs);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WriteSample(IntPtr p, int stream, IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockBuffer(IntPtr p, out IntPtr data, out int maximum, out int length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetLength(IntPtr p, int length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PointerArg(IntPtr p, IntPtr arg);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LongArg(IntPtr p, long value);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attrs, int count);
    [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(int size, out IntPtr buffer);
    [DllImport("mfplat.dll")] private static extern int MFCreateSample(out IntPtr sample);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSinkWriterFromURL(string path, IntPtr stream, IntPtr attrs, out IntPtr writer);
}
