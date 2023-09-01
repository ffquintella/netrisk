using Material.Icons;

namespace GUIClient.Models;

public class ListNode
{
    public string Name { get; set; }
    public int RelatedObjectId { get; set; }
    
    public MaterialIconKind? Icon { get; set; } = MaterialIconKind.Forbid;
    
    public ListNode(string name, int relatedObjectId, MaterialIconKind icon)
    {
        Name = name;
        RelatedObjectId = relatedObjectId;
        Icon = icon;
    }

}