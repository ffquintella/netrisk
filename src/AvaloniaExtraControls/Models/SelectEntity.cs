namespace AvaloniaExtraControls.Models;

public class SelectEntity
{
    public string Key { get; set; }
    public string Label { get; set; }
    
    public SelectEntity(string key, string label)
    {
        Key = key;
        Label = label;
    }
}