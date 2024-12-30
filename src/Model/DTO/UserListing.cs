namespace Model.DTO;

public class UserListing
{
    public int Id { get; set; } = -1;
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";

    public override string ToString()
    {
        return $"{Name} ({Id})";
    }
}