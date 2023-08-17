namespace Model.Entities;

public class EntityDto
{
    public int Id { get; set; }
    public string DefinitionName { get; set; } = null!;
    public string Status { get; set; } = null!;
    
    public List<EntitiesPropertyDto> EntitiesProperties { get; set; } = null!;
    public int? Parent { get; set; }
}