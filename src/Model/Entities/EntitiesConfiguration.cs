namespace Model.Entities;

public class EntitiesConfiguration
{

    public string Version { get; set; } = "0.1";

    public Dictionary<string, Dictionary<string, EntityType>> Definitions { get; set; } =
        new Dictionary<string, Dictionary<string, EntityType>>();

}