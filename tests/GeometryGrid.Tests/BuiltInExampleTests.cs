using System.IO;
using System.Text.Json;
using GeometryGrid.App.Services;
using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.Tests;

public sealed class BuiltInExampleTests
{
    private const string Resource = "GeometryGrid.App.Resources.tianjin_2026_q18.json";

    [Fact]
    public void EmbeddedExample_IsTheProvidedTianjinProblemWithoutPrivateMetadata()
    {
        var assembly = typeof(MainViewModel).Assembly;
        Assert.DoesNotContain(assembly.GetManifestResourceNames(), n => n.Contains("translated_geometry"));
        using var stream = assembly.GetManifestResourceStream(Resource);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();
        Assert.DoesNotMatch(@"[A-Za-z]:[\\/]|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", json);
        var document = ProjectSerializer.LoadJson(json, "built-in example");
        var result = new GeometryEngine().Evaluate(document);
        Assert.Empty(result.Errors);
        Assert.Empty(document.Parameters);
        Assert.Equal(7, document.Objects.Count);
        Assert.Equal("3", document.Objects.Single(p => p.Id == "B").X);
        Assert.Equal("4", document.Objects.Single(p => p.Id == "B").Y);
        Assert.Equal("7", document.Objects.Single(p => p.Id == "C").X);
        Assert.Equal("4/3", document.Objects.Single(p => p.Id == "C").Y);
        Assert.Collection(document.Solver.Targets,
            m => { Assert.Equal("M", m.Name); Assert.Equal("13/3", m.X); Assert.Equal("28/9", m.Y); },
            n => { Assert.Equal("N", n.Name); Assert.Equal("207/97", n.X); Assert.Equal("500/291", n.Y); });
        using var parsed = JsonDocument.Parse(json);
        var allowed = new[] { "schemaVersion", "canvas", "grid", "styles", "parameters", "objects", "solver", "exports" };
        Assert.All(parsed.RootElement.EnumerateObject(), p => Assert.Contains(p.Name, allowed));
    }

    [Fact]
    public void OpenDefaultExample_LoadsTianjinTargets()
    {
        var vm = new MainViewModel(new Dialogs());
        vm.OpenDefaultExample();
        Assert.NotNull(vm.Document);
        Assert.Equal(new[] { "M", "N" }, vm.Document!.Solver.Targets.Select(t => t.Name));
        Assert.Equal(7, vm.Document.Objects.Count);
        Assert.DoesNotContain("失败", vm.ValidationStatus);
    }

    private sealed class Dialogs : IFileDialogService
    {
        public string? OpenJson() => null;
        public string? SaveJson(string? currentPath) => null;
        public string? SaveExport(string extension, string filter) => null;
        public string? PickColor(string current) => null;
        public string? PromptText(string title, string prompt, string initialValue) => initialValue;
        public void ShowError(string title, string message) => throw new InvalidOperationException(title+": "+message);
    }
}
