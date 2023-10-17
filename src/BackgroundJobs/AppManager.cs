namespace BackgroundJobs;

public class AppManager
{
    public static ManualResetEvent QuitEvent = new ManualResetEvent(false);
}