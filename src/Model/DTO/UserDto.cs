namespace Model.DTO;

public class UserDto
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public bool Lockout { get; set; }
    public string Type { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime LastLogin { get; set; }
    public DateTime LastPasswordChangeDate { get; set; }
    public int RoleId { get; set; }
    public string? Lang { get; set; }
    public bool Admin { get; set; }
    public int MultiFactor { get; set; }
    public int ChangePassword { get; set; }
    public int Manager { get; set; }
    
}