using System.Net;
using System.Text.Json;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Model.DTO;
using Model.Exceptions;
using Model.Rest;
using RestSharp;

namespace ClientServices.Services;

public class IncidentResponsePlansRestService(IRestService restService)
    : RestServiceBase(restService), IIncidentResponsePlansService
{
    public async Task<List<IncidentResponsePlan>> GetAllAsync()
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest($"/IncidentResponsePlans");

        try
        {
            var response = await client.GetAsync<List<IncidentResponsePlan>>(request);

            if (response == null)
            {
                Logger.Error("Error listing incident response plans");
                throw new InvalidHttpRequestException("Error listing incident response plans", "/IncidentResponsePlans",
                    "GET");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing incident response plans message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing incident response plans", ex);
        }
    }

    public async Task<IncidentResponsePlan> CreateAsync(IncidentResponsePlan incidentResponsePlan)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans");

        try
        {
            request.AddJsonBody(incidentResponsePlan);

            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error creating incident response plan", opResult!);

            }

            var newIrp = JsonSerializer.Deserialize<IncidentResponsePlan>(response.Content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (newIrp == null)
            {
                Logger.Error("Error creating incident response plan ");
                throw new InvalidHttpRequestException("Error creating incident response plan", "/IncidentResponsePlans",
                    "POST");
            }

            return newIrp;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating incident response plan", ex);
        }
    }

    public async Task<IncidentResponsePlan> UpdateAsync(IncidentResponsePlan incidentResponsePlan)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{incidentResponsePlan.Id}");

        try
        {
            request.AddJsonBody(incidentResponsePlan);

            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
            {
                Logger.Error("Error updating incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error updating incident response plan", opResult!);

            }

            var newIrp = JsonSerializer.Deserialize<IncidentResponsePlan>(response.Content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (newIrp == null)
            {
                Logger.Error("Error updating incident response plan ");
                throw new InvalidHttpRequestException("Error updating incident response plan", "/IncidentResponsePlans",
                    "PUT");
            }

            return newIrp;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating incident response plan", ex);
        }
    }

    public async Task DeleteAsync(int id)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{id}");

        try
        {

            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error deleting incident response plan", opResult!);

            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting incident response plan", ex);
        }

    }

    public async Task<IncidentResponsePlan> GetByIdAsync(int id, bool includeTasks = false)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{id}");
        try
        {

            if (includeTasks) request.AddQueryParameter("includeTasks", "true");

            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting incident response plan by id ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting incident response plan by ip", opResult!);
            }

            var irp = JsonSerializer.Deserialize<IncidentResponsePlan>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (irp == null)
            {
                Logger.Error("Error getting incident response plan ");
                throw new InvalidHttpRequestException("Error getting incident response plan", "/IncidentResponsePlans",
                    "PUT");
            }

            return irp;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting incident response plan by id message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting incident response plan by id", ex);
        }
    }

    public async Task<IncidentResponsePlanTask> CreateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{incidentResponsePlanTask.PlanId}/Tasks");
        try
        {
            request.AddJsonBody(incidentResponsePlanTask);

            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating task for incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error creating task for incident response plan", opResult!);

            }

            var newTask = JsonSerializer.Deserialize<IncidentResponsePlanTask>(response.Content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (newTask == null)
            {
                Logger.Error("Error creating task for incident response plan ");
                throw new InvalidHttpRequestException("Error creating task for incident response plan",
                    "/IncidentResponsePlans", "POST");
            }

            return newTask;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating task for incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating task for incident response plan", ex);
        }
    }

    public async Task<IncidentResponsePlanTask> UpdateTaskAsync(IncidentResponsePlanTask incidentResponsePlanTask)
    {
        using var client = RestService.GetReliableClient();
        var request =
            new RestRequest(
                $"/IncidentResponsePlans/{incidentResponsePlanTask.PlanId}/Tasks/{incidentResponsePlanTask.Id}");
        try
        {
            request.AddJsonBody(incidentResponsePlanTask);

            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
            {
                Logger.Error("Error updating task for incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error updating task for incident response plan", opResult!);

            }

            var upTask = JsonSerializer.Deserialize<IncidentResponsePlanTask>(response.Content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (upTask == null)
            {
                Logger.Error("Error updating task for incident response plan ");
                throw new InvalidHttpRequestException("Error updating task for incident response plan",
                    "/IncidentResponsePlans", "PUT");
            }

            return upTask;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating task for incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating task for incident response plan", ex);
        }
    }

    public async Task<IncidentResponsePlanTask> GetTaskByIdAsync(int planId, int taskId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{planId}/Tasks/{taskId}");
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting task by id ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting task by id", opResult!);
            }

            var task = JsonSerializer.Deserialize<IncidentResponsePlanTask>(response.Content!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (task == null)
            {
                Logger.Error("Error getting task by id ");
                throw new InvalidHttpRequestException("Error getting task by id", "/IncidentResponsePlans", "PUT");
            }

            return task;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting task by id message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting task by id", ex);
        }
    }

    public async Task DeleteTaskAsync(int planId, int taskId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{planId}/Tasks/{taskId}");
        try
        {
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting task ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error deleting task", opResult!);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting task message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting task", ex);
        }
    }

    public async Task<List<IncidentResponsePlanTaskExecution>> GetTaskExecutionsByIdAsync(int planId, int taskId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{planId}/Tasks/{taskId}/Executions");
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting task executions by id ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting task executions by id", opResult!);
            }

            var taskExecutions = JsonSerializer.Deserialize<List<IncidentResponsePlanTaskExecution>>(response.Content!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (taskExecutions == null)
            {
                Logger.Error("Error getting task executions by id ");
                throw new InvalidHttpRequestException("Error getting task executions by id", "/IncidentResponsePlans",
                    "PUT");
            }

            return taskExecutions;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting task executions by id message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting task executions by id", ex);
        }
    }

    public async Task<IncidentResponsePlanTaskExecution> GetExecutionByTaskIdAsync(int planId, int taskId,
        int executionId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{planId}/Tasks/{taskId}/Executions/{executionId}");
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting execution by id ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting execution by id", opResult!);
            }

            var execution = JsonSerializer.Deserialize<IncidentResponsePlanTaskExecution>(response.Content!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (execution == null)
            {
                Logger.Error("Error getting execution by id ");
                throw new InvalidHttpRequestException("Error getting execution by id", "/IncidentResponsePlans", "PUT");
            }

            return execution;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting execution by id message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting execution by id", ex);
        }
    }

    public async Task<List<IncidentResponsePlanExecution>> GetExecutionsByPlanIdAsync(int planId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{planId}/Executions");
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting executions by id ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting executions by id", opResult!);
            }

            var executions = JsonSerializer.Deserialize<List<IncidentResponsePlanExecution>>(response.Content!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (executions == null)
            {
                Logger.Error("Error getting executions by id ");
                throw new InvalidHttpRequestException("Error getting executions by id", "/IncidentResponsePlans",
                    "PUT");
            }

            return executions;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting executions by id message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting executions by id", ex);
        }
    }

    public async Task<IncidentResponsePlanExecution> GetExecutionByIdAsync(int planId, int executionId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{planId}/Executions/{executionId}");
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting execution by id ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting execution by id", opResult!);
            }

            var execution = JsonSerializer.Deserialize<IncidentResponsePlanExecution>(response.Content!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (execution == null)
            {
                Logger.Error("Error getting execution by id ");
                throw new InvalidHttpRequestException("Error getting execution by id", "/IncidentResponsePlans", "PUT");
            }

            return execution;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting execution by id message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting execution by id", ex);
        }
    }

    public async Task<IncidentResponsePlanExecution> CreateExecutionAsync(
        IncidentResponsePlanExecution incidentResponsePlanExecution)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{incidentResponsePlanExecution.PlanId}/Executions");
        try
        {
            incidentResponsePlanExecution.Id = 0;
            request.AddJsonBody(incidentResponsePlanExecution);

            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating execution for incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error creating execution for incident response plan", opResult!);
            }

            var newExecution = JsonSerializer.Deserialize<IncidentResponsePlanExecution>(response.Content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (newExecution == null)
            {
                Logger.Error("Error creating execution for incident response plan ");
                throw new InvalidHttpRequestException("Error creating execution for incident response plan",
                    "/IncidentResponsePlans", "POST");
            }

            return newExecution;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating execution for incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating execution for incident response plan", ex);
        }
    }

    public async Task<IncidentResponsePlanTaskExecution> CreateTaskExecutionAsync(int planId,
        IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(
            $"/IncidentResponsePlans/{planId}/Tasks/{incidentResponsePlanTaskExecution.TaskId}/Executions");
        try
        {
            incidentResponsePlanTaskExecution.Id = 0;
            request.AddJsonBody(incidentResponsePlanTaskExecution);

            var response = await client.PostAsync(request);

            if (response.StatusCode != HttpStatusCode.Created || response.Content == null)
            {
                Logger.Error("Error creating task execution for incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error creating task execution for incident response plan", opResult!);
            }

            var newExecution = JsonSerializer.Deserialize<IncidentResponsePlanTaskExecution>(response.Content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (newExecution == null)
            {
                Logger.Error("Error creating task execution for incident response plan ");
                throw new InvalidHttpRequestException("Error creating task execution for incident response plan",
                    "/IncidentResponsePlans", "POST");
            }

            return newExecution;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error creating task execution for incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error creating task execution for incident response plan", ex);
        }
    }

    public async Task<IncidentResponsePlanExecution> UpdateExecutionAsync(
        IncidentResponsePlanExecution incidentResponsePlanExecution)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(
            $"/IncidentResponsePlans/{incidentResponsePlanExecution.PlanId}/Executions/{incidentResponsePlanExecution.Id}");
        try
        {
            request.AddJsonBody(incidentResponsePlanExecution);

            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
            {
                Logger.Error("Error updating execution for incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error updating execution for incident response plan", opResult!);
            }

            var upExecution = JsonSerializer.Deserialize<IncidentResponsePlanExecution>(response.Content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (upExecution == null)
            {
                Logger.Error("Error updating execution for incident response plan ");
                throw new InvalidHttpRequestException("Error updating execution for incident response plan",
                    "/IncidentResponsePlans", "PUT");
            }

            return upExecution;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating execution for incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating execution for incident response plan", ex);
        }
    }

    public async Task<IncidentResponsePlanTaskExecution> UpdateTaskExecutionAsync(int planId,
        IncidentResponsePlanTaskExecution incidentResponsePlanTaskExecution)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(
            $"/IncidentResponsePlans/{planId}/Tasks/{incidentResponsePlanTaskExecution.TaskId}/Executions/{incidentResponsePlanTaskExecution.Id}");
        try
        {
            request.AddJsonBody(incidentResponsePlanTaskExecution);

            var response = await client.PutAsync(request);

            if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
            {
                Logger.Error("Error updating task execution for incident response plan ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error updating task execution for incident response plan", opResult!);
            }

            var upExecution = JsonSerializer.Deserialize<IncidentResponsePlanTaskExecution>(response.Content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (upExecution == null)
            {
                Logger.Error("Error updating task execution for incident response plan ");
                throw new InvalidHttpRequestException("Error updating task execution for incident response plan",
                    "/IncidentResponsePlans", "PUT");
            }

            return upExecution;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error updating task execution for incident response plan message:{Message}", ex.Message);
            throw new RestComunicationException("Error updating task execution for incident response plan", ex);
        }
    }

    public async Task DeleteExecutionAsync(int planId, int executionId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{planId}/Executions/{executionId}");
        try
        {
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting execution ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error deleting execution", opResult!);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting execution message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting execution", ex);
        }
    }

    public async Task DeleteTaskExecutionAsync(int planId, int taskId, int incidentResponsePlanTaskExecutionId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest(
            $"/IncidentResponsePlans/{planId}/Tasks/{taskId}/Executions/{incidentResponsePlanTaskExecutionId}");
        try
        {
            var response = await client.DeleteAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting task execution ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error deleting task execution", opResult!);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error deleting task execution message:{Message}", ex.Message);
            throw new RestComunicationException("Error deleting task execution", ex);
        }
    }

    public async  Task<List<FileListing>> GetAttachmentsAsync(int incidentResponsePlanId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/IncidentResponsePlans/{incidentResponsePlanId}/Attachments");
        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error getting attachments ");

                var opResult = JsonSerializer.Deserialize<OperationError>(response!.Content!);

                throw new ErrorSavingException("Error getting attachments", opResult!);
            }

            var attachments = JsonSerializer.Deserialize<List<FileListing>>(response.Content!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (attachments == null)
            {
                Logger.Error("Error getting attachments ");
                throw new InvalidHttpRequestException("Error getting attachments", "/IncidentResponsePlans", "GET");
            }

            return attachments;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting attachments message:{Message}", ex.Message);
            throw new RestComunicationException("Error getting attachments", ex);
        }
    }

}