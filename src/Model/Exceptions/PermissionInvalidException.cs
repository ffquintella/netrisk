namespace Model.Exceptions;

public class PermissionInvalidException(string permission, int userId, string operationName)
    : Exception($"Permission {permission} is invalid for user {userId} at operation {operationName}")
{
    
    public string Permission { get; } = permission;
    public int UserId { get; } = userId;
    public string OperationName { get; } = operationName;
}