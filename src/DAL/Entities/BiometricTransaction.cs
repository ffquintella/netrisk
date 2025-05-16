using DAL.Enums;

namespace DAL.Entities;

public partial class BiometricTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int FaceIdUserId { get; set; }
    public string BiometricType { get; set; } = "FaceId";
    public DateTime DateTime { get; set; }
    public int? TransactionObjectId { get; set; }
    public string? TransactionObjectType { get; set; } = String.Empty;
    public string? TransactionDetails { get; set; } = String.Empty;
    public TransactionResult TransactionResult { get; set; } = TransactionResult.Unknown;
    
    public string BiometricLivenessAnchor { get; set; } = string.Empty;
    
    public virtual FaceIDUser? FaceIdUser { get; set; } = null!;
    public virtual User? User { get; set; } = null!;
    
}