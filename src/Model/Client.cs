using System;

namespace Model;

public class Client
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Hostname { get; set; }
    public string? LoggedAccount { get; set; }
    public DateTime RegistrationDate { get; set; }
    public string Status { get; set; } = null!;
    
    public override string ToString()
        => $"{Id} - {Name} - {Hostname} - {LoggedAccount} - {RegistrationDate} - {Status}";
}