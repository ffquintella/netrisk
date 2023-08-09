namespace Model.Entities;

public class EntityType
{
    public string Type { get; set; } = null!;
    
    public string Label { get; set; } = "";
    public Boolean Multiple { get; set; } = false;
    public int MaxSize { get; set; } = 0;
    public int MinSize { get; set; } = 0;
    public Boolean Nullable { get; set; } = false;
    public string? DefaultValue { get; set; } = null!;
    
}