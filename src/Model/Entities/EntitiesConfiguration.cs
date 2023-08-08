namespace Model.Entities;

public class EntitiesConfiguration
{

    public string Version { get; set; } = "0.1";

    public Dictionary<string, EntityDefinition> Definitions { get; set; } =
        new Dictionary<string,EntityDefinition>();

}