namespace GeometryGrid.App.Services;

public static class GpuAccelerationManager
{
    private static readonly object SyncRoot = new();
    private static GpuCandidateAccelerator? _accelerator;
    private static string _lastStatus = "尚未检测 GPU。";

    public static bool TryGet(
        out GpuCandidateAccelerator? accelerator,
        out string status)
    {
        lock (SyncRoot)
        {
            if (_accelerator is not null)
            {
                accelerator = _accelerator;
                status = $"可用：{_accelerator.DeviceName}";
                return true;
            }

            if (GpuCandidateAccelerator.TryCreate(
                    out var created,
                    out var createStatus))
            {
                _accelerator = created;
                _lastStatus = createStatus;
                accelerator = created;
                status = createStatus;
                return true;
            }

            _lastStatus = createStatus;
            accelerator = null;
            status = createStatus;
            return false;
        }
    }

    public static string LastStatus
    {
        get
        {
            lock (SyncRoot)
                return _lastStatus;
        }
    }
}
