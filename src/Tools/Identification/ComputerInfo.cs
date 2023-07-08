namespace Tools.Identification;

public static class ComputerInfo
{
    public static string GetComputerName()
    {
        return System.Environment.MachineName;
    }
    
    public static string GetOsVersion()
    {
        return System.Environment.OSVersion.ToString();
    }
    
    public static string GetLoggedUser()
    {
        return System.Environment.UserName;
    }
}