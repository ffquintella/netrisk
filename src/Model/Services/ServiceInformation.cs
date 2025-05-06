namespace Model.Services;

public class ServiceInformation
{
    public bool IsServiceAvailable { get; set; }
    public string ServiceName { get; set; }
    public string ServiceVersion { get; set; }
    public string ServiceDescription { get; set; }
    public string ServiceUrl { get; set; }
    public bool ServiceNeedsPlugin { get; set; }
    public bool ServicePluginInstalled { get; set; }
}