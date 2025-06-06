namespace Model.FaceID;

public class FaceTransactionData
{
    public int UserId { get; set; }
    public Guid TransactionId { get; set; }
    public DateTime StartTime { get; set; }
    
    public List<char> ValidationSequence { get; set; } = new List<char>();
}