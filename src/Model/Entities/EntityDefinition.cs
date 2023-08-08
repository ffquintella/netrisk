namespace Model.Entities;

public class EntityDefinition
{
    public Boolean IsRoot { get; set; }
    
    public Dictionary<string, EntityType> Properties { get; set; } = new Dictionary<string, EntityType>();
}