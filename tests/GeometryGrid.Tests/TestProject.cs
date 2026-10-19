using GeometryGrid.Core.Models;
using GeometryGrid.Core.Serialization;
using System.IO;

namespace GeometryGrid.Tests;

internal static class TestProject
{
    public static string ExamplePath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "examples", "translated_geometry_8x8.json");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new FileNotFoundException("找不到 examples/translated_geometry_8x8.json。");
        }
    }

    public static ProjectDocument Load() => ProjectSerializer.Load(ExamplePath);
}
