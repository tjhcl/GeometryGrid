using GeometryGrid.Core.Models;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.App.ViewModels;

internal static class ConstructionAnimationFormatting
{
    public static void Apply(
        ProjectDocument document,
        ProjectDocument currentArtwork,
        bool includeMissingObjects)
    {
        var artwork = ProjectSerializer.Clone(currentArtwork);
        document.Canvas = artwork.Canvas;
        document.Grid = artwork.Grid;
        document.Styles = artwork.Styles;

        var artworkById = artwork.Objects.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var existingIds = document.Objects
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var item in document.Objects)
        {
            if (!artworkById.TryGetValue(item.Id, out var saved))
                continue;
            item.Name = saved.Name;
            item.Style = saved.Style;
            item.Label = saved.Label;
            CopyEditablePosition(item, saved);
        }

        if (!includeMissingObjects)
            return;

        foreach (var saved in artwork.Objects)
            if (existingIds.Add(saved.Id))
                document.Objects.Add(saved);
    }

    private static void CopyEditablePosition(
        GeometryObjectDefinition destination,
        GeometryObjectDefinition source)
    {
        if (!string.Equals(destination.Type, source.Type, StringComparison.Ordinal) ||
            !source.Draggable)
            return;

        destination.Draggable = true;
        switch (source.Type)
        {
            case "fixedPoint":
            case "parametricPoint":
                destination.X = source.X;
                destination.Y = source.Y;
                destination.ParameterX = source.ParameterX;
                destination.ParameterY = source.ParameterY;
                break;
            case "pointOnGridLine":
            case "pointOnSegment":
            case "pointOnLine":
            case "pointOnCircle":
            case "pointOnArc":
                destination.Parameter = source.Parameter;
                break;
        }
    }
}
