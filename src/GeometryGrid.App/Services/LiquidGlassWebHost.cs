using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GeometryGrid.App.Services;

/// <summary>
/// Hosts the upstream martin65536/liquid-glass-webgl renderer under the WPF
/// visual tree. The browser files are embedded in the single EXE and extracted
/// into LocalAppData at runtime; no remote page or network resource is loaded.
/// </summary>
internal sealed class LiquidGlassWebHost : IAsyncDisposable
{
    private const string VirtualHost = "geometrygrid.liquid-glass";
    private const string RendererCommit = "a05dc75e781873c4fbcfb5fc14ec99fa3902cc23";
    private const string AssetRevision = "3.01";
    private readonly WebView2CompositionControl _view;
    private bool _initialized;
    private bool _enabled;

    public event EventHandler? Ready;

    public LiquidGlassWebHost(WebView2CompositionControl view)
    {
        _view = view;
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            _view.Visibility = Visibility.Collapsed;
            if (_initialized && _view.CoreWebView2 is { } activeCore)
            {
                try
                {
                    await activeCore.TrySuspendAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Liquid Glass suspend fallback: {ex}");
                }
            }
            return;
        }

        _view.Visibility = Visibility.Visible;
        if (!_initialized)
        {
            await InitializeAsync();
            return;
        }

        if (_view.CoreWebView2 is { } core)
        {
            core.Resume();
            _ = _view.Dispatcher.BeginInvoke(() => Ready?.Invoke(this, EventArgs.Empty));
        }
    }

    private async Task InitializeAsync()
    {
        if (_initialized)
            return;

        try
        {
            var webRoot = ExtractEmbeddedWebAssets();
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GeometryGrid",
                "WebView2");
            Directory.CreateDirectory(dataRoot);

            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: dataRoot);
            await _view.EnsureCoreWebView2Async(environment);

            var core = _view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnProcessFailed;
            core.Navigate($"https://{VirtualHost}/index.html");
            _initialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Liquid Glass WebGL fallback: {ex}");
            _view.Visibility = Visibility.Collapsed;
        }
    }

    private static string ExtractEmbeddedWebAssets()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeometryGrid",
            "WebGlass",
            $"{RendererCommit[..12]}-{AssetRevision}");
        Directory.CreateDirectory(root);

        var assembly = Assembly.GetExecutingAssembly();
        Extract(assembly, "GeometryGrid.App.WebGlass.index.html", Path.Combine(root, "index.html"));
        Extract(
            assembly,
            "GeometryGrid.App.WebGlass.liquid-glass-runtime.js",
            Path.Combine(root, "liquid-glass-runtime.js"));
        return root;
    }

    private static void Extract(Assembly assembly, string resourceName, string destination)
    {
        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        if (File.Exists(destination) && new FileInfo(destination).Length == source.Length)
            return;

        using var target = File.Create(destination);
        source.CopyTo(target);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        Debug.WriteLine($"Liquid Glass WebGL: {e.WebMessageAsJson}");
        if (_enabled &&
            e.WebMessageAsJson.Contains("liquid-glass-ready", StringComparison.Ordinal))
            _view.Dispatcher.BeginInvoke(() => Ready?.Invoke(this, EventArgs.Empty));
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Debug.WriteLine($"Liquid Glass WebGL process failed: {e.ProcessFailedKind}");
        _view.Visibility = Visibility.Collapsed;
    }

    public ValueTask DisposeAsync()
    {
        if (_initialized && _view.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
            core.ProcessFailed -= OnProcessFailed;
        }
        _view.Dispose();
        return ValueTask.CompletedTask;
    }
}
