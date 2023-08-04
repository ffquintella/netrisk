namespace Model.Exceptions;

public class EntityDefinitionNotFoundException: Exception
{
    public String DefinitionName { get; }
    
    public EntityDefinitionNotFoundException(string definitionName): base($"Entity definition;{definitionName} not found in configurations")
    {
        DefinitionName = definitionName;
    }
}