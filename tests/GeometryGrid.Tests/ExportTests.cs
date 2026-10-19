using GeometryGrid.Core.Geometry;
using GeometryGrid.Export;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Rendering;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace GeometryGrid.Tests;

public sealed class ExportTests
{
    [Theory]
    [InlineData(1d, 3840, 2160)]
    [InlineData(10d / 9d, 3840, 2160)]
    [InlineData(1.5d, 3840, 2160)]
    [InlineData(.75d, 2160, 3840)]
    public void VideoExport_StandardUhdEnvelopeSupportsCanvasRatios(double ratio, int width, int height)
    {
        Assert.Equal((width, height), AnimatedVideoExporter.GetCompatibleFrameSize(3840, ratio));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        try
        {
            RunSta(() => AnimatedVideoExporter.Export([new AnimatedGifFrame(Scene(), 20)],
                path, width, height, 60, contentAspectRatio: ratio));
            Assert.Contains("avc1", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)));
            var directory = Environment.GetEnvironmentVariable("GEOMETRYGRID_VIDEO_QA_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
                File.Copy(path, Path.Combine(directory, $"uhd-canvas-{ratio.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}.mp4"), true);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void VideoFrame_SquareCanvasIsNotStretchedOrCropped()
    {
        RunSta(() =>
        {
            var scene = new RenderScene
            {
                Canvas = new CanvasSettings { MinX=0, MaxX=8, MinY=0, MaxY=8, Padding=40, Background="#FFFFFF" },
                Grid = new GridSettings { Visible=false, Color="#000000", BorderWidth=4 }
            };
            var bitmap = RasterExporter.RenderVideoFrame(scene, 1920, 1080, 1);
            var pixels = new byte[1920 * 1080 * 4]; bitmap.CopyPixels(pixels, 1920 * 4, 0);
            Assert.False(IsDark(pixels, 1920, 100, 540));
            Assert.True(IsDark(pixels, 1920, 460, 540));
            Assert.True(IsDark(pixels, 1920, 1460, 540));
            Assert.True(IsDark(pixels, 1920, 960, 40));
            Assert.True(IsDark(pixels, 1920, 960, 1040));
        });
    }

    [Theory]
    [InlineData(1920, 1080, 30)]
    [InlineData(3840, 2160, 60)]
    public void VideoExport_WritesH264Mp4AtHdResolution(int width, int height, int fps)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        try
        {
            RunSta(() => AnimatedVideoExporter.Export(
                [new AnimatedGifFrame(Scene(), 20)], path, width, height, fps));
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 1000);
            var container = System.Text.Encoding.Latin1.GetString(bytes);
            Assert.Contains("ftyp", container);
            Assert.Contains("moov", container);
            Assert.Contains("avc1", container);
            var directory = Environment.GetEnvironmentVariable("GEOMETRYGRID_VIDEO_QA_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                File.Copy(path, Path.Combine(directory, $"codec-{width}x{height}-{fps}.mp4"), true);
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void VideoExport_RejectsOddDimensionsAndPreservesDestinationOnCancellation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");
        File.WriteAllText(path, "original");
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimatedVideoExporter.Export(
                [new AnimatedGifFrame(Scene(), 100)], path, 641, 480));
            RunSta(() => Assert.Throws<OperationCanceledException>(() => AnimatedVideoExporter.Export(
                [new AnimatedGifFrame(Scene(), 100)], path, 640, 480, 30,
                cancellationToken: new CancellationToken(true))));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void PngExport_IsNonEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        RunSta(() => RasterExporter.ExportPng(Scene(), path, 600, 600, 96, false));
        try
        {
            Assert.True(new FileInfo(path).Length > 1000);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, File.ReadAllBytes(path)[..4]);
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(600, frame.PixelWidth);
            Assert.Equal(600, frame.PixelHeight);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JpegExport_IsNonEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        RunSta(() => RasterExporter.ExportJpeg(Scene(), path, 600, 600, 96, 90, "#FFFFFF"));
        try
        {
            Assert.True(new FileInfo(path).Length > 1000);
            Assert.Equal(new byte[] { 0xFF, 0xD8 }, File.ReadAllBytes(path)[..2]);
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(600, frame.PixelWidth);
            Assert.Equal(600, frame.PixelHeight);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GifExport_WritesMultipleAnimatedFrames()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gif");
        RunSta(() =>
        {
            var scene = Scene();
            AnimatedGifExporter.Export(
                [
                    new AnimatedGifFrame(scene, 8),
                    new AnimatedGifFrame(scene, 120),
                ],
                path,
                400,
                400,
                96);
        });
        try
        {
            var header = File.ReadAllBytes(path)[..6];
            Assert.True(
                header.SequenceEqual("GIF87a"u8.ToArray()) ||
                header.SequenceEqual("GIF89a"u8.ToArray()));

            RunSta(() =>
            {
                using var stream = File.OpenRead(path);
                var decoder = new GifBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                Assert.Equal(2, decoder.Frames.Count);
                Assert.Equal((ushort)8, ((BitmapMetadata)decoder.Frames[0].Metadata).GetQuery("/grctlext/Delay"));
                Assert.Equal((ushort)120, ((BitmapMetadata)decoder.Frames[1].Metadata).GetQuery("/grctlext/Delay"));
            });
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EmfExport_HasEmfHeaderAndVectorRecords()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.emf");
        EmfExporter.Export(Scene(), path, 600, 600, "text");
        try
        {
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 500);
            Assert.Equal(1, BitConverter.ToInt32(bytes, 0));
            Assert.Equal(new byte[] { 0x20, 0x45, 0x4D, 0x46 }, bytes[40..44]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportVisualScale_GrowsWithOutputResolution()
    {
        var canvas = TestProject.Load().Canvas;
        var small = new SceneLayout(canvas, 800, 800);
        var large = new SceneLayout(canvas, 1600, 1600);
        Assert.Equal(1, small.VisualScale, 10);
        Assert.True(large.VisualScale > 2);
        Assert.Equal(large.Scale / small.Scale, large.VisualScale, 10);
    }

    [Fact]
    public void VisualScale_DoesNotChangeWithCanvasMathematicalRange()
    {
        var original = TestProject.Load().Canvas;
        var resized = new GeometryGrid.Core.Models.CanvasSettings
        {
            MinX = original.MinX,
            MinY = original.MinY,
            MaxX = 30,
            MaxY = 18,
            Padding = original.Padding
        };
        var before = new SceneLayout(original, 1280, 900);
        var after = new SceneLayout(resized, 1280, 900);

        Assert.Equal(before.VisualScale, after.VisualScale, 12);
        Assert.NotEqual(before.Scale, after.Scale);
    }

    [Fact]
    public void RasterRender_At144DpiKeepsRightAndBottomBordersInsideBitmap()
    {
        RunSta(() =>
        {
            var scene = new RenderScene
            {
                Canvas = new CanvasSettings
                {
                    MinX = 0,
                    MaxX = 8,
                    MinY = 0,
                    MaxY = 8,
                    Padding = 40,
                    Background = "#FFFFFF"
                },
                Grid = new GridSettings
                {
                    Visible = false,
                    Color = "#000000",
                    BorderWidth = 4
                }
            };
            var bitmap = RasterExporter.Render(scene, 600, 600, 144, false);
            var pixels = new byte[600 * 600 * 4];
            bitmap.CopyPixels(new Int32Rect(0, 0, 600, 600), pixels, 600 * 4, 0);

            Assert.True(IsDark(pixels, 600, 540, 300), "右边框在 144 DPI 导出中缺失或被裁切。");
            Assert.True(IsDark(pixels, 600, 300, 540), "下边框在 144 DPI 导出中缺失或被裁切。");
        });
    }

    [Fact]
    public void OptionalVisualQaExports_AreProducedWhenRequested()
    {
        var directory = Environment.GetEnvironmentVariable("GEOMETRYGRID_QA_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "qa_geometry.png", "qa_geometry.jpg", "qa_geometry.emf", "qa_emf_preview.png" })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
        RunSta(() =>
        {
            RasterExporter.ExportPng(Scene(), Path.Combine(directory, "qa_geometry.png"), 1600, 1600, 144, false);
            RasterExporter.ExportJpeg(Scene(), Path.Combine(directory, "qa_geometry.jpg"), 1600, 1600, 144, 92, "#FFFFFF");
        });
        var emfPath = Path.Combine(directory, "qa_geometry.emf");
        EmfExporter.Export(Scene(), emfPath, 1600, 1600, "text");
        using var metafile = System.Drawing.Image.FromFile(emfPath);
        using var preview = new System.Drawing.Bitmap(1600, 1600);
        using (var graphics = System.Drawing.Graphics.FromImage(preview))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.DrawImage(metafile, 0, 0, 1600, 1600);
        }
        preview.Save(Path.Combine(directory, "qa_emf_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static GeometryGrid.Core.Rendering.RenderScene Scene() =>
        new GeometryEngine().Evaluate(TestProject.Load()).Scene;

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { exception = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null) throw exception;
    }

    private static bool IsDark(byte[] pixels, int width, int x, int y)
    {
        var offset = (y * width + x) * 4;
        return pixels[offset] < 80 && pixels[offset + 1] < 80 && pixels[offset + 2] < 80;
    }
}
