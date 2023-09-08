namespace Model.ClientData;

public class ClientInformation
{
    public string Version { get; set; } = "0.1";
    public Dictionary<string, string> DownloadLocation { get; set; } = new Dictionary<string, string>();
}