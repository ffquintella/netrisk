namespace Model.Services;

public class ServiceInformation
{
    public bool IsServiceAvailable { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceVersion { get; set; } = string.Empty;
    public string ServiceDescription { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public bool ServiceNeedsPlugin { get; set; }
    public bool ServicePluginInstalled { get; set; }
}