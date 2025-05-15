namespace DAL.Entities;

public partial class FaceIDUser
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool IsEnabled { get; set; }
    public string SignatureSeed { get; set; } = string.Empty;
    
    public string FaceIdentification { get; set; } = string.Empty;
    
    public DateTime LastUpdate { get; set; }
    
    public int LastUpdateUserId { get; set; }
    
    public virtual User? LastUpdateUser { get; set; } = null!;
    public virtual User User { get; set; }
    
    
}