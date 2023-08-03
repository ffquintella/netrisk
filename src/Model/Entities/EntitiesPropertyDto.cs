namespace Model.Entities;

public class EntitiesPropertyDto
{
    public int Id { get; set; }
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string Name { get; set; } = null!;
}