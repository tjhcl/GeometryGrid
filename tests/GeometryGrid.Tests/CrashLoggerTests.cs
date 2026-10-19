using GeometryGrid.App;
using System.IO;

namespace GeometryGrid.Tests;

public sealed class CrashLoggerTests
{
    [Fact]
    public void CrashLogger_WritesDetailedLogBesideApplication()
    {
        var path = CrashLogger.TryWrite(
            new InvalidOperationException("crash-log-test"),
            "unit-test",
            terminating: false);

        Assert.NotNull(path);
        Assert.Equal("crash", new DirectoryInfo(Path.GetDirectoryName(path!)!).Name);
        var text = File.ReadAllText(path!);
        Assert.Contains("GeometryGrid crash report", text);
        Assert.Contains("Source: unit-test", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("crash-log-test", text);
        File.Delete(path!);
    }
}
