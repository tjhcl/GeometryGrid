namespace GeometryGrid.App.ViewModels;

public sealed class ObjectEntryViewModel(MainViewModel owner, string id) : ObservableObject
{
    public string Id { get; } = id;
    private GeometryGrid.Core.Models.GeometryObjectDefinition? Definition =>
        owner.Document?.Objects.FirstOrDefault(o => o.Id == Id);

    public string Type => Definition?.Type ?? "";
    public string DisplayName => $"{owner.GetObjectDisplayName(Id)}  ·  {Id}  ·  {Type}";
    public bool IsVisible
    {
        get => owner.GetObjectVisible(Id);
        set => owner.SetObjectVisible(Id, value);
    }
    public double Opacity
    {
        get => owner.GetObjectOpacity(Id);
        set => owner.SetObjectOpacity(Id, value);
    }
    public string Status
    {
        get
        {
            var result = owner.Evaluation?.Results.GetValueOrDefault(Id);
            return result is null ? "未计算" : result.IsValid ? "有效" : result.Error ?? "无效";
        }
    }
    public bool IsValid => owner.Evaluation?.Results.GetValueOrDefault(Id)?.IsValid ?? false;

    public void Refresh() => Raise(nameof(Type), nameof(DisplayName), nameof(IsVisible), nameof(Opacity), nameof(Status), nameof(IsValid));
}
