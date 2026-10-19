using System.IO;
using GeometryGrid.App.Services;
using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Serialization;
using GeometryGrid.Export;

namespace GeometryGrid.Tests;

public sealed class ConstructionVideoTests
{
    [Fact]
    public void Timeline_PreservesProjectAndProvidesReadableStages()
    {
        var vm = new MainViewModel(new Dialogs());
        vm.OpenCommand.Execute(null);
        var problem = ConstructionSearchProblemBuilder.Build(vm.Document!);
        var report = new ConstructionSearchEngine().Search(problem.Points, problem.Elements, problem.Targets,
            new ConstructionSearchOptions(MaxSteps: 4, MaxSolutions: 1, MaxStates: 100_000,
                UseCompass: false, MaxPointsPerState: 240, MinX: 0, MaxX: 8, MinY: 0, MaxY: 8));
        var solution = Assert.Single(report.Solutions);
        vm.SelectedConstructionSolution = new ConstructionSolutionEntryViewModel(solution);
        var before = System.Text.Json.JsonSerializer.Serialize(vm.Document, ProjectSerializer.Options);
        var frames = vm.CreateConstructionAnimationFrames(solution, 30);
        var after = System.Text.Json.JsonSerializer.Serialize(vm.Document, ProjectSerializer.Options);
        Assert.Equal(before, after);
        Assert.Equal(0, vm.ConstructionPlaybackStep);
        Assert.All(frames, f => Assert.False(string.IsNullOrEmpty(f.Scene.AnimationCaption)));
        Assert.Contains(frames, f => f.Scene.AnimationCaption!.Contains("选择第一个点"));
        Assert.Contains(frames, f => f.Scene.AnimationCaption!.Contains("确认新交点"));
        Assert.Equal((int)Math.Round((3.4 * solution.StepCount + 3.4) * 100), frames.Sum(f => f.DelayCentiseconds));
        Assert.Contains("构造完成", frames[^1].Scene.AnimationCaption!);
        var smoothFrames = vm.CreateConstructionAnimationFrames(solution, 60, .75);
        Assert.Equal(17 / .75, smoothFrames.Sum(f => f.PreciseDurationSeconds!.Value), 8);
        Assert.Contains(smoothFrames, f => f.PreciseDurationSeconds < .02);

        var directory = Environment.GetEnvironmentVariable("GEOMETRYGRID_VIDEO_QA_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var gifFrames = vm.CreateConstructionAnimationFrames(solution);
        var uhdFrames = Environment.GetEnvironmentVariable("GEOMETRYGRID_UHD_ANIMATION_QA") == "1"
            ? vm.CreateConstructionAnimationFrames(solution, 60) : null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                AnimatedVideoExporter.Export(frames, Path.Combine(directory, "construction-hd.mp4"), 1920, 1920, 30);
                AnimatedGifExporter.Export(gifFrames, Path.Combine(directory, "construction-preview.gif"), 720, 720);
                if (uhdFrames is not null)
                    AnimatedVideoExporter.Export(uhdFrames, Path.Combine(directory, "construction-4k60.mp4"),
                        3840, 2160, 60, contentAspectRatio: 1);
                foreach (var (name, frame) in new[] { ("initial", frames[0]), ("drawing", frames[30]),
                    ("intersection", frames[85]), ("complete", frames[^1]) })
                    RasterExporter.ExportPng(frame.Scene, Path.Combine(directory, $"{name}.png"), 960, 960, 96, false);
                File.WriteAllText(Path.Combine(directory, "timeline.txt"),
                    $"steps={solution.StepCount}\nframes={frames.Count}\nseconds={frames.Sum(f => f.DelayCentiseconds) / 100d}\n");
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        Assert.Null(error);
    }

    private sealed class Dialogs : IFileDialogService
    {
        public string? OpenJson() => TestProject.ExamplePath;
        public string? SaveJson(string? currentPath) => null;
        public string? SaveExport(string extension, string filter) => null;
        public string? PickColor(string current) => null;
        public string? PromptText(string title, string prompt, string initialValue) => initialValue;
        public void ShowError(string title, string message) => throw new InvalidOperationException(title + ": " + message);
    }
}
