using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Assessments;
using Model.Exceptions;
using RestSharp;
using Tools.String;
using File = System.IO.File;

namespace ClientServices.Services;

/// <summary>
/// REST client for the comment and evidence on each answer of an assessment run (GitHub #80, T297, S44).
///
/// Every call goes through the error-reporting client, as the continuity client does: the writes answer
/// 400/403/409 with a reason (<c>run_submitted</c>, <c>evidence_limit_reached</c>, an invalid field) that
/// the viewer turns into a message, and the reliable client throws on any non-2xx before the body can be
/// read. None of the writes is retried.
///
/// The upload content is staged through <see cref="IFilesService.StageUploadAsync"/> — the same chunked
/// transport every attachment uses — and completed by the evidence endpoint.
/// </summary>
public class AssessmentEvidenceRestService(IRestService restService, IFilesService filesService)
    : RestServiceBase(restService), IAssessmentEvidenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static string RunRoute(int runId) => $"/Assessments/runs/{runId}";

    public async Task<AssessmentRunAnswer> SaveCommentAsync(int runId, int questionId, string? comment) =>
        Read<AssessmentRunAnswer>(await ExecuteAsync($"{RunRoute(runId)}/questions/{questionId}/comment",
            Method.Put, new AssessmentAnswerCommentRequest { Comment = comment }));

    public async Task<List<AssessmentAnswerEvidence>> GetRunEvidenceAsync(int runId) =>
        Read<List<AssessmentAnswerEvidence>>(await ExecuteAsync($"{RunRoute(runId)}/evidence", Method.Get));

    public async Task<AssessmentAnswerEvidence> UploadEvidenceAsync(int runId, int questionId, Uri filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!filePath.IsFile || !File.Exists(filePath.LocalPath))
            throw new ArgumentException("Uri is not a file", nameof(filePath));

        // The server enforces the same bound; checking here first spares the assessor a 20 MiB upload
        // that would only be refused at the end.
        var length = new FileInfo(filePath.LocalPath).Length;
        if (!AssessmentEvidencePolicy.IsSizeAccepted(length))
            throw new InvalidParameterException("file", length == 0
                ? "The file is empty."
                : $"The file is {length} bytes; the limit is {AssessmentEvidencePolicy.MaxEvidenceBytes} bytes.");

        var type = await filesService.ResolveUploadTypeAsync(filePath.AbsolutePath);
        var content = await File.ReadAllBytesAsync(filePath.LocalPath);
        var staged = await filesService.StageUploadAsync(content);

        var request = new AssessmentEvidenceUploadRequest
        {
            FileId = staged.FileId,
            TotalChunks = staged.TotalChunks,
            Name = StringCleaner.CleanEmptyChars(Path.GetFileName(filePath.LocalPath)),
            Type = type.Value.ToString()
        };

        return Read<AssessmentAnswerEvidence>(await ExecuteAsync(
            $"{RunRoute(runId)}/questions/{questionId}/evidence", Method.Post, request));
    }

    public async Task DeleteEvidenceAsync(int runId, int questionId, string uniqueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueName);

        await ExecuteAsync(
            $"{RunRoute(runId)}/questions/{questionId}/evidence/{Uri.EscapeDataString(uniqueName)}",
            Method.Delete);
    }

    // --- transport --------------------------------------------------------------------------

    private static T Read<T>(RestResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidHttpRequestException("The server answered with an empty body.",
                response.ResponseUri?.AbsolutePath ?? "", response.Request.Method.ToString());

        return JsonSerializer.Deserialize<T>(response.Content, JsonOptions)
               ?? throw new InvalidHttpRequestException("The server answered with an unreadable body.",
                   response.ResponseUri?.AbsolutePath ?? "", response.Request.Method.ToString());
    }

    private async Task<RestResponse> ExecuteAsync(string route, Method method, object? body = null)
    {
        using var client = RestService.GetClient(reportErrorResponses: true);

        var request = new RestRequest(route);
        if (body != null) request.AddJsonBody(body);

        try
        {
            var response = await client.ExecuteAsync(request, method);

            // No status at all means the server was never reached — the transport failure, distinct
            // from the server saying no.
            if (response.StatusCode == 0)
                throw new RestComunicationException($"Error calling {route}",
                    response.ErrorException ?? new HttpRequestException(response.ErrorMessage));

            Reject(route, method, response.StatusCode, response.Content);

            return response;
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode is { } status)
            {
                Reject(route, method, status, null);
                throw new InvalidHttpRequestException($"Error calling {route}", route, method.ToString());
            }

            Logger.Error("Error calling {Route} message:{Message}", route, ex.Message);
            throw new RestComunicationException($"Error calling {route}", ex);
        }
    }

    /// <summary>
    /// 404 is a not-found; 400, 403 and 409 carry the server's reason and are passed through; anything
    /// else that is not a success is a generic failure.
    /// </summary>
    private void Reject(string route, Method method, HttpStatusCode status, string? content)
    {
        if (status == HttpStatusCode.NotFound)
            throw new DataNotFoundException(route, route, new Exception("Not found"));

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.Forbidden)
        {
            Logger.Warning("{Method} {Route} refused with {Status}", method, route, status);
            throw new InvalidHttpRequestException(
                string.IsNullOrWhiteSpace(content) ? $"Error calling {route}" : content, route,
                method.ToString());
        }

        if (status is not (HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent))
        {
            Logger.Error("Error calling {Route}: {Status}", route, status);
            throw new InvalidHttpRequestException($"Error calling {route}", route, method.ToString());
        }
    }
}
