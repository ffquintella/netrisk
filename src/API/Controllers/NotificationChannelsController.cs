using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Notifications;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Notification channel administration (Track 4 milestone 4.1.2).
///
/// Everything here requires the configuration permission: a channel holds a credential that can post
/// into somebody's Slack, and its delivery log names findings. There is deliberately no endpoint that
/// returns a channel's webhook URL or signing secret in clear — reads return the redaction placeholder,
/// and a write that sends the placeholder back keeps the stored value.
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class NotificationChannelsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    INotificationSubscriptionsService service,
    INotificationChannelRegistry registry,
    INotificationDispatcher dispatcher)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>The channel kinds this build can deliver through, for the admin form's picker.</summary>
    [HttpGet]
    [Route("providers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ChannelProviderView>))]
    public ActionResult<List<ChannelProviderView>> GetProviders()
    {
        GetUser();

        return Ok(registry.All
            .Select(c => new ChannelProviderView { Kind = c.Kind, Name = c.Name })
            .ToList());
    }

    /// <summary>The event catalog a subscription may listen for.</summary>
    [HttpGet]
    [Route("events")]
    [ProducesResponseType(StatusCodes.Status200OK,
        Type = typeof(IReadOnlyList<NotificationCatalog.EventDescriptor>))]
    public ActionResult<IReadOnlyList<NotificationCatalog.EventDescriptor>> GetEvents()
    {
        GetUser();
        return Ok(NotificationCatalog.Events);
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NotificationChannel>))]
    public Task<ActionResult<List<NotificationChannel>>> GetAll([FromQuery] bool includeDisabled = true)
    {
        GetUser();
        return RunAsync(() => service.GetChannelsAsync(includeDisabled), "listing notification channels");
    }

    [HttpGet]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NotificationChannel))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<NotificationChannel>> Get(int id)
    {
        GetUser();
        return RunAsync(() => service.GetChannelAsync(id), $"reading notification channel {id}");
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(NotificationChannel))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<NotificationChannel>> Create([FromBody] NotificationChannel channel)
    {
        var user = GetUser();

        return CreatedAsync(() => service.CreateChannelAsync(channel, user.Value),
            created => $"NotificationChannels/{created.Id}", "creating a notification channel");
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NotificationChannel))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<NotificationChannel>> Update(int id, [FromBody] NotificationChannel channel)
    {
        var user = GetUser();

        return RunAsync(() =>
        {
            // The route wins over the body: a mismatched id in a PUT body is a client bug, and honouring
            // it would let a request update a channel other than the one it addressed.
            channel.Id = id;
            return service.UpdateChannelAsync(channel, user.Value);
        }, $"updating notification channel {id}");
    }

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(int id)
    {
        GetUser();
        return RunAsync(() => service.DeleteChannelAsync(id), $"deleting notification channel {id}");
    }

    /// <summary>
    /// Sends a real test message through the channel (4.1.2). A real send rather than a reachability
    /// probe: a webhook URL that resolves but posts into the wrong workspace passes a ping.
    /// </summary>
    [HttpPost]
    [Route("{id:int}/test")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ChannelTestResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ChannelTestResult>> Test(int id)
    {
        var user = GetUser();

        Logger.Information("User:{User} sent a test message through notification channel {Channel}",
            user.Value, id);

        return RunAsync(() => dispatcher.TestChannelAsync(id), $"testing notification channel {id}");
    }
}

/// <summary>One registered delivery provider, for the admin form.</summary>
public class ChannelProviderView
{
    public NotificationChannelKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;
}
