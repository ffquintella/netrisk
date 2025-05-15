namespace DAL.Entities;

public partial class FaceIDUser
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool IsEnabled { get; set; }
    public string SignatureSeed { get; set; } = string.Empty;
    
    public virtual User User { get; set; }
}