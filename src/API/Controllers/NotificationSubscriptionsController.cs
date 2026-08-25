using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Event-triggered notification subscriptions and the delivery log
/// (Track 4 milestone 4.1.3).
///
/// The delivery log is the half of this milestone people actually ask for: "the SLA breach fired, did
/// the team hear about it?" cannot be answered from the absence of a Slack message.
/// </summary>
[PermissionAuthorize("configuration")]
[ApiController]
[Route("[controller]")]
public class NotificationSubscriptionsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    INotificationSubscriptionsService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NotificationSubscription>))]
    public Task<ActionResult<List<NotificationSubscription>>> GetAll()
    {
        GetUser();
        return RunAsync(service.GetSubscriptionsAsync, "listing notification subscriptions");
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(NotificationSubscription))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ActionResult<NotificationSubscription>> Create(
        [FromBody] NotificationSubscription subscription)
    {
        var user = GetUser();

        Logger.Information("User:{User} subscribed channel {Channel} to {Event}",
            user.Value, subscription?.ChannelId, subscription?.EventType);

        return CreatedAsync(() => service.CreateSubscriptionAsync(subscription!),
            created => $"NotificationSubscriptions/{created.Id}", "creating a notification subscription");
    }

    [HttpPut]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NotificationSubscription))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<NotificationSubscription>> Update(int id,
        [FromBody] NotificationSubscription subscription)
    {
        GetUser();

        return RunAsync(() =>
        {
            subscription.Id = id;
            return service.UpdateSubscriptionAsync(subscription);
        }, $"updating notification subscription {id}");
    }

    [HttpDelete]
    [Route("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Delete(int id)
    {
        GetUser();
        return RunAsync(() => service.DeleteSubscriptionAsync(id),
            $"deleting notification subscription {id}");
    }

    /// <summary>
    /// The delivery log, newest first. <paramref name="status"/> filters it — <c>Failed</c> is the one
    /// an operator opens it for.
    /// </summary>
    [HttpGet]
    [Route("deliveries")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NotificationDelivery>))]
    public Task<ActionResult<List<NotificationDelivery>>> GetDeliveries(
        [FromQuery] int limit = 200,
        [FromQuery] NotificationDeliveryStatus? status = null,
        [FromQuery] int? subscriptionId = null)
    {
        GetUser();

        return RunAsync(() => service.GetDeliveriesAsync(limit, status, subscriptionId),
            "reading the notification delivery log");
    }

    /// <summary>
    /// Re-queues a failed delivery — the "resend" an operator wants after fixing a webhook URL.
    /// Refuses a delivery that already went out, so the button cannot duplicate an alert.
    /// </summary>
    [HttpPost]
    [Route("deliveries/{id:int}/requeue")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(NotificationDelivery))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<NotificationDelivery>> Requeue(int id)
    {
        var user = GetUser();

        Logger.Information("User:{User} requeued notification delivery {Delivery}", user.Value, id);

        return RunAsync(() => service.RequeueDeliveryAsync(id), $"requeuing delivery {id}");
    }
}
