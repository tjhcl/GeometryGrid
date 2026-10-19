using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace GeometryGrid.App;

internal static class CrashLogger
{
    private static readonly object Gate = new();
    private static readonly HashSet<Exception> LoggedExceptions = new(ReferenceEqualityComparer.Instance);

    public static string? TryWrite(Exception exception, string source, bool terminating)
    {
        lock (Gate)
        {
            if (!LoggedExceptions.Add(exception)) return null;
        }

        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "crash");
            Directory.CreateDirectory(directory);
            var now = DateTimeOffset.Now;
            var path = Path.Combine(directory,
                $"crash-{now:yyyyMMdd-HHmmss-fff}-pid{Environment.ProcessId}.log");
            var assembly = Assembly.GetExecutingAssembly();
            var productVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                 ?? assembly.GetName().Version?.ToString()
                                 ?? "unknown";
            var content = new StringBuilder()
                .AppendLine("GeometryGrid crash report")
                .AppendLine("=========================")
                .AppendLine($"Time: {now:O}")
                .AppendLine($"Source: {source}")
                .AppendLine($"Terminating: {terminating}")
                .AppendLine($"Version: {productVersion}")
                .AppendLine($"Process: {Environment.ProcessPath}")
                .AppendLine($"Process ID: {Environment.ProcessId}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"64-bit process: {Environment.Is64BitProcess}")
                .AppendLine($".NET: {Environment.Version}")
                .AppendLine($"Current culture: {CultureInfo.CurrentCulture.Name}")
                .AppendLine($"UI culture: {CultureInfo.CurrentUICulture.Name}")
                .AppendLine()
                .AppendLine("Exception")
                .AppendLine("---------")
                .AppendLine(exception.ToString())
                .ToString();
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch
        {
            // Crash logging must never replace the original exception.
            return null;
        }
    }
}
