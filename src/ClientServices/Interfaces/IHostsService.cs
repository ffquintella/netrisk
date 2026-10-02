using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IHostsService
{
    /// <summary>
    /// Get one host
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Host? GetOne(int id);
    
    /// <summary>
    /// Get all hosts
    /// </summary>
    /// <returns></returns>
    public List<Host> GetAll();
    
    /// <summary>
    /// Get all Hosts
    /// </summary>
    /// <returns></returns>
    public Task<List<Host>> GetAllAsync();
    
    
    /// <summary>The sort <see cref="GetFilteredAsync(int,int,string?)"/> asks the server for.</summary>
    public const string DefaultSort = "hostName";

    /// <summary>
    /// One page of hosts, sorted by host name on the server. Kept for callers that do not page;
    /// it is <see cref="GetFilteredAsync(int,int,string?,string?)"/> with <see cref="DefaultSort"/>,
    /// minus the total.
    /// </summary>
    public Task<List<Host>> GetFilteredAsync(int pageSize, int pageNumber, string? filter);

    /// <summary>
    /// One page of hosts plus the number of hosts the filter matches across every page, read from
    /// the server's <c>X-Total-Count</c> header (S38 §5.5).
    /// </summary>
    /// <param name="pageSize">Rows per page; the server clamps it.</param>
    /// <param name="pageNumber">1-based page.</param>
    /// <param name="filter">Sieve-syntax filter, e.g. <c>hostName@=web,status==42,criticality==5</c>.
    /// Null or empty for none. Filterable: hostName, id, status, fqdn, ip, os, teamId,
    /// registrationDate, criticality, environment, owner, source, riskScore, lastVerificationDate.</param>
    /// <param name="sorts">Sieve-syntax sort, <c>-</c> prefix for descending (<c>-riskScore</c>).
    /// Null or empty leaves the order to the server.</param>
    /// <exception cref="Model.Exceptions.BadFilterException">The server rejected the filter or sort
    /// (HTTP 400/409) — an unknown field or a malformed expression.</exception>
    public Task<(List<Host> Items, int Total)> GetFilteredAsync(int pageSize, int pageNumber, string? filter,
        string? sorts);

    /// <summary>
    /// The distinct, non-blank environment values of the hosts the user can see, ordered — the
    /// environment facet's options (S38 §5.2).
    /// </summary>
    public Task<List<string>> GetEnvironmentsAsync();

    /// <summary>
    /// Open-vulnerability counts by severity for one host (S38 §5.3).
    /// </summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">The host does not exist or the user
    /// cannot see it.</exception>
    public Task<HostVulnerabilitySummaryDto> GetVulnerabilitySummaryAsync(int hostId);

    /// <summary>
    /// The host's field-level change history, newest first (S38 §5.4). Rows sharing a
    /// <see cref="AuditLog.CorrelationId"/> were written by one save. <see cref="AuditLog.User"/> is
    /// never populated; <see cref="AuditLog.Actor"/> and <see cref="AuditLog.UserId"/> say who acted.
    /// </summary>
    /// <param name="hostId">The host.</param>
    /// <param name="limit">At most this many rows, 1–5000.</param>
    /// <exception cref="Model.Exceptions.DataNotFoundException">The host does not exist or the user
    /// cannot see it.</exception>
    public Task<List<AuditLog>> GetHistoryAsync(int hostId, int limit = 500);
    
    /// <summary>
    /// Create a new host
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public Task<Host?> Create(Host host);


    /// <summary>
    /// Update a host
    /// </summary>
    /// <param name="host"></param>
    public void UpdateAsync(Host host);
    
    /// <summary>
    /// Check if the host exists
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public bool HostExists(string hostIp);
    
    
    /// <summary>
    /// Check if the host exists
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public Task<bool> HostExistsAsync(string hostIp);
    
    
    /// <summary>
    /// Get host by ip
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public Host? GetByIp(string hostIp);
    
    /// <summary>
    /// Get host by ip
    /// </summary>
    /// <param name="hostIp"></param>
    /// <returns></returns>
    public Task<Host> GetByIpAsync(string hostIp);
    
    /// <summary>
    /// Delete a host
    /// </summary>
    /// <param name="hostId"></param>
    public void Delete(int hostId);
    
    /// <summary>
    /// Gets a host service
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="serviceId"></param>
    /// <returns></returns>
    public HostsService GetHostService(int hostId, int serviceId);
    
    /// <summary>
    /// Get all host services
    /// </summary>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public List<HostsService> GetAllHostService(int hostId);
    public Task<List<HostsService>> GetAllHostServiceAsync(int hostId);
    
    
    /// <summary>
    /// Get all host vulnerabilities
    /// </summary>
    /// <param name="hostId"></param>
    /// <returns></returns>
    public List<Vulnerability> GetAllHostVulnerabilities(int hostId);
    
    public Task<List<Vulnerability>> GetAllHostVulnerabilitiesAsync(int hostId);
    
    
    
    /// <summary>
    /// Check if host has the specified service
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="name"></param>
    /// <param name="port"></param>
    /// <param name="protocol"></param>
    /// <returns></returns>
    public Task<bool> HostHasServiceAsync(int hostId, string name, int? port, string protocol);
    
    /// <summary>
    /// Create and add a service to a host
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="service"></param>
    /// <returns></returns>
    public Task<HostsService> CreateAndAddServiceAsync(int hostId, HostsServiceDto service);
    
    /// <summary>
    /// Delete a service from a host
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="serviceId"></param>
    public void DeleteService(int hostId, int serviceId);
    
    /// <summary>
    /// Update a service from a host
    /// </summary>
    /// <param name="hostId"></param>
    /// <param name="service"></param>
    public void UpdateService(int hostId, HostsServiceDto service);
    
    public Task<HostsService?> FindServiceAsync(int hostId, string name, int? port, string protocol);
}