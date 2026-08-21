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
            Logger.Error("Error listing IRP templates message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing IRP templates", ex);
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
            Logger.Error("Error getting IRP template message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting IRP template", ex);
        }
    }

    public async Task<IrpTemplate> CreateAsync(IrpTemplate template)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(BasePath);
        request.AddJsonBody(ToTemplateRequest(template));

        try
        {
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating IRP template");
                var opResult = JsonSerializer.Deserialize<OperationError>(response.Content ?? "{}");
                throw new ErrorSavingException("Error creating IRP template", opResult!);
            }

            return JsonSerializer.Deserialize<IrpTemplate>(response.Content, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating IRP template message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating IRP template", ex);
        }
    }

    public async Task<IrpTemplate> UpdateAsync(IrpTemplate template)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{template.Id}");
        request.AddJsonBody(ToTemplateRequest(template));

        try
        {
            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
            {
                Logger.Error("Error updating IRP template {Id}", template.Id);
                var opResult = JsonSerializer.Deserialize<OperationError>(response.Content ?? "{}");
                throw new ErrorSavingException("Error updating IRP template", opResult!);
            }

            return JsonSerializer.Deserialize<IrpTemplate>(response.Content, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating IRP template message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating IRP template", ex);
        }
    }

    public async Task DeleteAsync(int id)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{id}");

        try
        {
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting IRP template {Id}", id);
                throw new InvalidHttpRequestException("Error deleting IRP template", $"{BasePath}/{id}", "DELETE");
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting IRP template message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting IRP template", ex);
        }
    }

    public async Task<IrpTemplate> CloneAsync(int id)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{id}/Clone");

        try
        {
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error cloning IRP template {Id}", id);
                var opResult = JsonSerializer.Deserialize<OperationError>(response.Content ?? "{}");
                throw new ErrorSavingException("Error cloning IRP template", opResult!);
            }

            return JsonSerializer.Deserialize<IrpTemplate>(response.Content, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error cloning IRP template message:{Message}", ex.Message);
            throw new RestComunicationException("Error cloning IRP template", ex);
        }
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
            Logger.Error("Error listing IRP template tasks message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing IRP template tasks", ex);
        }
    }

    public async Task<IrpTemplateTask> CreateTaskAsync(int templateId, IrpTemplateTask task)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{templateId}/Tasks");
        request.AddJsonBody(ToTaskRequest(task));

        try
        {
            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating task on IRP template {Id}", templateId);
                var opResult = JsonSerializer.Deserialize<OperationError>(response.Content ?? "{}");
                throw new ErrorSavingException("Error creating IRP template task", opResult!);
            }

            return JsonSerializer.Deserialize<IrpTemplateTask>(response.Content, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating IRP template task message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating IRP template task", ex);
        }
    }

    public async Task<IrpTemplateTask> UpdateTaskAsync(int templateId, IrpTemplateTask task)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{templateId}/Tasks/{task.Id}");
        request.AddJsonBody(ToTaskRequest(task));

        try
        {
            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
            {
                Logger.Error("Error updating task {TaskId} on IRP template {Id}", task.Id, templateId);
                var opResult = JsonSerializer.Deserialize<OperationError>(response.Content ?? "{}");
                throw new ErrorSavingException("Error updating IRP template task", opResult!);
            }

            return JsonSerializer.Deserialize<IrpTemplateTask>(response.Content, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating IRP template task message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating IRP template task", ex);
        }
    }

    public async Task DeleteTaskAsync(int templateId, int taskId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"{BasePath}/{templateId}/Tasks/{taskId}");

        try
        {
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting task {TaskId} on IRP template {Id}", taskId, templateId);
                throw new InvalidHttpRequestException("Error deleting IRP template task",
                    $"{BasePath}/{templateId}/Tasks/{taskId}", "DELETE");
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting IRP template task message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting IRP template task", ex);
        }
    }

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
