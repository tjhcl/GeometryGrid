using GeometryGrid.Core.Models;

namespace GeometryGrid.Core.Geometry;

public sealed class DependencyGraph
{
    private readonly Dictionary<string, HashSet<string>> _dependencies;
    private readonly Dictionary<string, HashSet<string>> _dependents;

    public DependencyGraph(IEnumerable<GeometryObjectDefinition> objects)
    {
        var list = objects.ToList();
        var ids = list.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        _dependencies = list.ToDictionary(o => o.Id, GetReferences, StringComparer.Ordinal);
        foreach (var (id, dependencies) in _dependencies)
        {
            var missing = dependencies.Where(d => !ids.Contains(d)).ToList();
            if (missing.Count > 0) throw new DependencyException($"对象 {id} 引用了不存在的 ID：{string.Join(", ", missing)}。");
        }

        _dependents = ids.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (id, dependencies) in _dependencies)
            foreach (var dependency in dependencies)
                _dependents[dependency].Add(id);
    }

    public IReadOnlyList<string> TopologicalOrder()
    {
        var remaining = _dependencies.ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);
        var queue = new Queue<string>(remaining.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<string>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);
            foreach (var dependent in _dependents[id])
            {
                remaining[dependent]--;
                if (remaining[dependent] == 0) queue.Enqueue(dependent);
            }
        }
        if (order.Count != remaining.Count)
            throw new DependencyException($"检测到循环依赖：{string.Join(", ", remaining.Where(kv => kv.Value > 0).Select(kv => kv.Key))}。");
        return order;
    }

    public IReadOnlySet<string> AffectedBy(string id)
    {
        var affected = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (_dependents.ContainsKey(id)) queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!affected.Add(current)) continue;
            foreach (var dependent in _dependents[current]) queue.Enqueue(dependent);
        }
        return affected;
    }

    public static HashSet<string> GetReferences(GeometryObjectDefinition item)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in new[] { item.PointA, item.PointB, item.PointC, item.Center, item.ObjectA, item.ObjectB, item.Support, item.ClosestTo })
            if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
        return result;
    }
}

public sealed class DependencyException(string message) : Exception(message);
