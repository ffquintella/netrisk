using System;

namespace Model.Exceptions;

public class DataNotFoundException(
    string databaseName,
    string identification,
    Exception? innerException = null)
    : Exception($"Data of type {databaseName} not found by id {identification}", innerException)
{
    public String DatabaseName { get; } = databaseName;
    public String Identification { get; } = identification;
}