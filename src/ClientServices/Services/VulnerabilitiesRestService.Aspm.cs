using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Model.Exceptions;
using Model.Findings;
using RestSharp;

namespace ClientServices.Services;

/// <summary>
/// Track 3 (ASPM) client calls: importer discovery, dynamic imports, import status, and the finding
/// triage lifecycle.
///
/// Split into a partial so the pre-Track-3 service stays readable; the plumbing (client, error
/// handling, deserialisation) follows the same shape as the rest of the file.
/// </summary>
public partial class VulnerabilitiesRestService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Everything the server can import with, for the import dialog's picker.</summary>
    public async Task<List<ImporterDescriptor>> GetImportersAsync()
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest("/Vulnerabilities/importers");

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error listing vulnerability importers");
                throw new InvalidHttpRequestException("Error listing vulnerability importers",
                    "/Vulnerabilities/importers", "GET");
            }

            return JsonSerializer.Deserialize<List<ImporterDescriptor>>(response.Content!, JsonOptions) ?? [];
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing vulnerability importers message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing vulnerability importers", ex);
        }
    }

    /// <summary>
    /// Starts an import of an already-uploaded file. <paramref name="importerName"/> may be
    /// <c>auto</c>, which asks the server to detect the format.
    /// </summary>
    public async Task<ImportJobStatus> StartImportAsync(string importerName, string fileId,
        bool ignoreNegligible = true)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest($"/Vulnerabilities/import/{importerName}/{fileId}");
        request.AddQueryParameter("ignoreNegligible", ignoreNegligible.ToString().ToLowerInvariant());

        try
        {
            var response = await client.PostAsync(request);

            // A 404 here means the importer name is unknown, and the body carries the list of names
            // that would have worked — worth surfacing rather than flattening into "not found".
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidHttpRequestException(
                    response.Content ?? "Unknown importer",
                    $"/Vulnerabilities/import/{importerName}/{fileId}", "POST");

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error starting a {Importer} import", importerName);
                throw new InvalidHttpRequestException("Error starting the import",
                    $"/Vulnerabilities/import/{importerName}/{fileId}", "POST");
            }

            return JsonSerializer.Deserialize<ImportJobStatus>(response.Content!, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error starting a {Importer} import message:{Message}", importerName, ex.Message);
            throw new RestComunicationException("Error starting the import", ex);
        }
    }

    /// <summary>The status and counts of an import, for the dialog's progress and summary.</summary>
    public async Task<ScanImport> GetImportAsync(int importId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/Vulnerabilities/import-jobs/{importId}");

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DataNotFoundException("scan_imports", importId.ToString(),
                    new Exception("Import not found"));

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException("Error reading the import",
                    $"/Vulnerabilities/import-jobs/{importId}", "GET");

            return JsonSerializer.Deserialize<ScanImport>(response.Content!, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error reading import {Import} message:{Message}", importId, ex.Message);
            throw new RestComunicationException("Error reading the import", ex);
        }
    }

    /// <summary>Recent imports, newest first.</summary>
    public async Task<List<ScanImport>> GetImportsAsync(int take = 50)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest("/Vulnerabilities/import-jobs");
        request.AddQueryParameter("take", take.ToString());

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException("Error listing imports", "/Vulnerabilities/import-jobs", "GET");

            return JsonSerializer.Deserialize<List<ScanImport>>(response.Content!, JsonOptions) ?? [];
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error listing imports message:{Message}", ex.Message);
            throw new RestComunicationException("Error listing imports", ex);
        }
    }

    /// <summary>
    /// Moves a finding through the triage lifecycle. A 422 means the transition is not allowed from
    /// the finding's current state; the server's explanation is passed through so the UI can show it.
    /// </summary>
    public async Task<Vulnerability> UpdateLifecycleStatusAsync(int findingId, FindingStatus status,
        string? justification = null, int? duplicateOfId = null)
    {
        using var client = RestService.GetReliableClient();

        var request = new RestRequest($"/Vulnerabilities/{findingId}/status");
        request.AddJsonBody(new
        {
            status,
            justification,
            duplicateOfId
        });

        try
        {
            var response = await client.PutAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new DataNotFoundException("vulnerabilities", findingId.ToString(),
                    new Exception("Finding not found"));

            if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
                throw new InvalidHttpRequestException(response.Content ?? "The transition was refused",
                    $"/Vulnerabilities/{findingId}/status", "PUT");

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException("Error changing the finding status",
                    $"/Vulnerabilities/{findingId}/status", "PUT");

            return JsonSerializer.Deserialize<Vulnerability>(response.Content!, JsonOptions)!;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error changing the status of finding {Finding} message:{Message}", findingId, ex.Message);
            throw new RestComunicationException("Error changing the finding status", ex);
        }
    }

    /// <summary>The finding's audit timeline, newest first.</summary>
    public async Task<List<FindingStatusHistory>> GetStatusHistoryAsync(int findingId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/Vulnerabilities/{findingId}/history");

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException("Error reading the finding history",
                    $"/Vulnerabilities/{findingId}/history", "GET");

            return JsonSerializer.Deserialize<List<FindingStatusHistory>>(response.Content!, JsonOptions) ?? [];
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error reading the history of finding {Finding} message:{Message}", findingId, ex.Message);
            throw new RestComunicationException("Error reading the finding history", ex);
        }
    }

    /// <summary>Which states the finding may move to, so the UI offers only legal actions.</summary>
    public async Task<List<FindingStatus>> GetAllowedTransitionsAsync(int findingId)
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest($"/Vulnerabilities/{findingId}/allowed-transitions");

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException("Error reading the allowed transitions",
                    $"/Vulnerabilities/{findingId}/allowed-transitions", "GET");

            return JsonSerializer.Deserialize<List<FindingStatus>>(response.Content!, JsonOptions) ?? [];
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error reading allowed transitions for finding {Finding} message:{Message}", findingId,
                ex.Message);
            throw new RestComunicationException("Error reading the allowed transitions", ex);
        }
    }

    /// <summary>SLA compliance by severity, for the dashboard widget.</summary>
    public async Task<List<SlaComplianceView>> GetSlaComplianceAsync()
    {
        using var client = RestService.GetReliableClient();
        var request = new RestRequest("/Vulnerabilities/sla/compliance");

        try
        {
            var response = await client.GetAsync(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidHttpRequestException("Error reading SLA compliance",
                    "/Vulnerabilities/sla/compliance", "GET");

            return JsonSerializer.Deserialize<List<SlaComplianceView>>(response.Content!, JsonOptions) ?? [];
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error reading SLA compliance message:{Message}", ex.Message);
            throw new RestComunicationException("Error reading SLA compliance", ex);
        }
    }
}
