namespace Model.Entities;

public class EntityDefinition
{
    public Boolean IsRoot { get; set; }
    
    public List<string>? AllowedChildren { get; set; } = new List<string>();
    public Dictionary<string, EntityType> Properties { get; set; } = new Dictionary<string, EntityType>();
}