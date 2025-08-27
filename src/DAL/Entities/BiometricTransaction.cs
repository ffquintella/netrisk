using DAL.Enums;

namespace DAL.Entities;

public partial class BiometricTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int FaceIdUserId { get; set; }
    public string BiometricType { get; set; } = "FaceId";
    
    public DateTime StartTime { get; set; }
    public DateTime ResultTime { get; set; }
    
    public int? TransactionObjectId { get; set; }
    public string? TransactionObjectType { get; set; } = String.Empty;
    public string? TransactionDetails { get; set; } = String.Empty;
    
    public Guid? TransactionId { get; set; }
    
    public string? TransactionResultDetails { get; set; } = String.Empty;
    
    public List<char> ValidationSequence { get; set; } = new List<char>();
    
    public string? ValidationObjectData { get; set; } = string.Empty;
    
    public TransactionResult TransactionResult { get; set; } = TransactionResult.Unknown;
    public string BiometricLivenessAnchor { get; set; } = string.Empty;
    
    public virtual FaceIDUser? FaceIdUser { get; set; } = null!;
    public virtual User? User { get; set; } = null!;
    
}