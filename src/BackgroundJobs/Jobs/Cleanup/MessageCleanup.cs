using Model;
using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Cleanup;

/// <summary>
/// Cleans up old messages.
/// </summary>
public class MessageCleanup:  BaseJob, IJob
{
    
    public MessageCleanup(ILogger logger, DALService dal): base(logger, dal)
    {
        
    }

    public void Run()
    {
        using var context = DalService.GetContext();
       
        // Deleting messages that are read and older then 2 days
        var messages = context.Messages.Where(m =>  m.Status == (int) IntStatus.Read &&  m.ReceivedAt < DateTime.Now.AddDays(-2));
        
        context.Messages.RemoveRange(messages);
        context.SaveChanges();
        
        Log.Information("Cleaned messages read older then 2 days");
        
        // Now removing all messages that are older then 48 days
        
        var messagesOlder = context.Messages.Where(m => m.ReceivedAt < DateTime.Now.AddDays(-48));
        
        context.Messages.RemoveRange(messagesOlder);
        context.SaveChanges();
        
        Log.Information("Cleaned messages older then 48 days");
    }
}