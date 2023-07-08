namespace Model.Authentication;

public class SAMLRequest
{
    public string RequestToken { get; set; } = "";
    public string Status { get; set; } = "requested";

    public string UserName { get; set; } = "";
}