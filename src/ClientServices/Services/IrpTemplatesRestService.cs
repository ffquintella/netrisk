using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using Model.Rest;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// REST client for IRP templates (Track 2 milestone 2.4.1).
/// </summary>
public class IrpTemplatesRestService(IRestService restService)
    : RestServiceBase(restService), IIrpTemplatesService
{
    private const string BasePath = "/IrpTemplates";

    public async Task<List<IrpTemplate>> GetAllAsync()
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(BasePath);

        try
        {
            var response = await client.GetAsync<List<IrpTemplate>>(request);

            if (response == null)
            {
                Logger.Error("Error listing IRP templates");
                throw new InvalidHttpRequestException("Error listing IRP templates", BasePath, "GET");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw Communication("Error listing IRP templates", ex);
        }
    }

    public async Task<IrpTemplate> GetByIdAsync(int id)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{id}");

        try
        {
            var response = await client.GetAsync<IrpTemplate>(request);

            if (response == null)
            {
                Logger.Error("Error getting IRP template {Id}", id);
                throw new InvalidHttpRequestException("Error getting IRP template", $"{BasePath}/{id}", "GET");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw Communication("Error getting IRP template", ex);
        }
    }

    public async Task<IrpTemplate> CreateAsync(IrpTemplate template)
    {
        var response = await WriteAsync(BasePath, Method.Post, ToTemplateRequest(template),
            HttpStatusCode.Created, "Error creating IRP template");

        return JsonSerializer.Deserialize<IrpTemplate>(response.Content!, JsonOptions)!;
    }

    public async Task<IrpTemplate> UpdateAsync(IrpTemplate template)
    {
        var response = await WriteAsync($"{BasePath}/{template.Id}", Method.Put, ToTemplateRequest(template),
            HttpStatusCode.OK, "Error updating IRP template");

        return JsonSerializer.Deserialize<IrpTemplate>(response.Content!, JsonOptions)!;
    }

    public async Task DeleteAsync(int id)
        => await DeleteAsync($"{BasePath}/{id}", "Error deleting IRP template");

    public async Task<IrpTemplate> CloneAsync(int id)
    {
        var response = await WriteAsync($"{BasePath}/{id}/Clone", Method.Post, body: null,
            HttpStatusCode.Created, "Error cloning IRP template");

        return JsonSerializer.Deserialize<IrpTemplate>(response.Content!, JsonOptions)!;
    }

    public async Task<List<IrpTemplateTask>> GetTasksAsync(int templateId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{templateId}/Tasks");

        try
        {
            var response = await client.GetAsync<List<IrpTemplateTask>>(request);

            if (response == null)
            {
                Logger.Error("Error listing tasks of IRP template {Id}", templateId);
                throw new InvalidHttpRequestException("Error listing IRP template tasks",
                    $"{BasePath}/{templateId}/Tasks", "GET");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw Communication("Error listing IRP template tasks", ex);
        }
    }

    public async Task<IrpTemplateTask> CreateTaskAsync(int templateId, IrpTemplateTask task)
    {
        var response = await WriteAsync($"{BasePath}/{templateId}/Tasks", Method.Post, ToTaskRequest(task),
            HttpStatusCode.Created, "Error creating IRP template task");

        return JsonSerializer.Deserialize<IrpTemplateTask>(response.Content!, JsonOptions)!;
    }

    public async Task<IrpTemplateTask> UpdateTaskAsync(int templateId, IrpTemplateTask task)
    {
        var response = await WriteAsync($"{BasePath}/{templateId}/Tasks/{task.Id}", Method.Put, ToTaskRequest(task),
            HttpStatusCode.OK, "Error updating IRP template task");

        return JsonSerializer.Deserialize<IrpTemplateTask>(response.Content!, JsonOptions)!;
    }

    public async Task DeleteTaskAsync(int templateId, int taskId)
        => await DeleteAsync($"{BasePath}/{templateId}/Tasks/{taskId}", "Error deleting IRP template task");

    /// <summary>
    /// Performs a write and reports what the server actually said.
    ///
    /// Two deliberate choices, both of them the difference between an operator reading the server's
    /// sentence and reading "Request failed with status code BadRequest":
    /// <list type="bullet">
    /// <item><c>reportErrorResponses</c>, because the default client sets RestSharp's
    /// <c>ThrowOnAnyError</c> and raises before the status can be inspected — with the body already
    /// gone. Every status check below is unreachable code without it.</item>
    /// <item><c>ExecuteAsync</c> rather than the <c>PostAsync</c>/<c>PutAsync</c> extensions, which
    /// call <c>ThrowIfError</c> and undo the same thing.</item>
    /// </list>
    /// This screen is where that cost was paid: a task save refused by the acyclicity check in
    /// <c>IrpTemplatesController.ValidatePredecessorAsync</c> names the offending predecessor, and
    /// none of it reached the log.
    /// </summary>
    private async Task<RestResponse> WriteAsync(string route, Method method, object? body,
        HttpStatusCode expected, string failureMessage)
    {
        using var client = MutatingClient(reportErrorResponses: true);
        var request = new RestRequest(route);
        if (body != null) request.AddJsonBody(body);

        var response = await ExecuteAsync(client, request, method, route, failureMessage);

        if (response.StatusCode == expected && response.Content != null) return response;

        var error = TryReadOperationError(response) ?? DescribeRefusal(response);

        Logger.Error("{Failure}: the server answered {Status} — {Detail}",
            failureMessage, (int)response.StatusCode, error.Title);

        throw new ErrorSavingException(failureMessage, error);
    }

    /// <summary>
    /// A delete, which the API answers with 204 and older deployments with 200.
    ///
    /// Kept apart from <see cref="WriteAsync"/> because a refused delete has no saved entity to
    /// report — the callers expect <see cref="InvalidHttpRequestException"/> — but it goes through
    /// the same error-reporting client so the reason still reaches the log.
    /// </summary>
    private async Task DeleteAsync(string route, string failureMessage)
    {
        using var client = MutatingClient(reportErrorResponses: true);
        var request = new RestRequest(route);

        var response = await ExecuteAsync(client, request, Method.Delete, route, failureMessage);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK) return;

        Logger.Error("{Failure}: the server answered {Status} — {Detail}",
            failureMessage, (int)response.StatusCode, DescribeRefusal(response).Title);

        throw new InvalidHttpRequestException(failureMessage, route, "DELETE");
    }

    /// <summary>
    /// Sends the request and separates the three failures a caller must not confuse: the server was
    /// never reached, the session is gone, and the server broke. Anything else is handed back for
    /// the caller's own status check.
    /// </summary>
    private async Task<RestResponse> ExecuteAsync(IRestClient client, RestRequest request, Method method,
        string route, string failureMessage)
    {
        RestResponse response;

        try
        {
            response = await client.ExecuteAsync(request, method);
        }
        catch (HttpRequestException ex)
        {
            // Reachable when the caller was handed a throwing client after all — the reliable
            // wrapper throws before the status check regardless of these options.
            throw Communication(failureMessage, ex);
        }

        // No status at all means the request never arrived; that is the transport failure, and it is
        // not the same thing as a server that answered.
        if (response.StatusCode == 0)
        {
            Logger.Error("{Failure} message:{Detail}", failureMessage,
                response.ErrorMessage ?? response.ErrorException?.Message);

            throw new RestComunicationException(failureMessage,
                response.ErrorException ?? new HttpRequestException(response.ErrorMessage));
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized) throw SessionExpired(failureMessage, route);

        if ((int)response.StatusCode >= 500)
        {
            Logger.Error("{Failure}: the server answered {Status}", failureMessage, (int)response.StatusCode);

            throw new RestComunicationException(failureMessage,
                response.ErrorException
                ?? new HttpRequestException($"Request failed with status code {response.StatusCode}",
                    null, response.StatusCode));
        }

        return response;
    }

    /// <summary>
    /// Wraps a transport-level failure, naming an expired session as one.
    ///
    /// A 401 arrives here rather than as a refusal because the API challenges with a redirect that
    /// <c>AuthChallengeHandler</c> translates — before that translation an expired session was
    /// reported as a rejected save, which is what sent an operator looking for the mistake in the
    /// task they had just typed.
    /// </summary>
    private RestComunicationException Communication(string failureMessage, HttpRequestException ex)
    {
        if (ex.StatusCode == HttpStatusCode.Unauthorized)
            return SessionExpired(failureMessage, route: null);

        Logger.Error("{Failure} message:{Detail}", failureMessage, ex.Message);

        return new RestComunicationException(failureMessage, ex);
    }

    private RestComunicationException SessionExpired(string failureMessage, string? route)
    {
        Logger.Error("{Failure}: the server asked for a new sign-in — the session has expired{Route}",
            failureMessage, route == null ? "" : $" ({route})");

        return new RestComunicationException(
            $"{failureMessage}: the session has expired, sign in again",
            new HttpRequestException("Request failed with status code Unauthorized", null,
                HttpStatusCode.Unauthorized));
    }

    /// <summary>
    /// The refusal as an <see cref="OperationError"/> when the body is not one.
    ///
    /// <c>BadRequest("A task cannot depend on itself")</c> serializes as a bare JSON string, so the
    /// structured read returns null for exactly the messages worth showing.
    /// </summary>
    private static OperationError DescribeRefusal(RestResponse response) => new()
    {
        Status = (int)response.StatusCode,
        Title = string.IsNullOrWhiteSpace(response.Content)
            ? $"The server answered {(int)response.StatusCode} {response.StatusCode}"
            : response.Content.Trim('"')
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The API takes a flat request body, not the entity: posting the entity would drag its
    /// Tasks navigation along and the server would try to re-key rows it already owns.
    /// </summary>
    private static object ToTemplateRequest(IrpTemplate template) => new
    {
        template.Name,
        template.Description,
        template.MatchingRulesJson,
        template.IsEnabled
    };

    private static object ToTaskRequest(IrpTemplateTask task) => new
    {
        task.Title,
        task.InstructionsMarkdown,
        task.AssigneeRuleJson,
        task.DueOffsetSeconds,
        task.PredecessorTaskId,
        task.RequiresConfirmation
    };
}
