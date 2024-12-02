using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using DAL.Entities;
using NSubstitute;
using RestSharp;

namespace ClientServices.Tests.Mock;

public class MockHostsRestService
{
    public static void ConfigureMocks(ref IRestClient mockClient)
    {        
        mockClient.ExecuteAsync(Arg.Is<RestRequest>(rq => rq.Resource == "/Hosts/1/Services"), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetHostsService())  ,
                ContentType = "application/json",
                ContentLength = 2
            });
        
        mockClient.ExecuteAsync(Arg.Is<RestRequest>(rq => rq.Resource == "/Hosts/1/Vulnerabilities"), Arg.Any<CancellationToken>())
            .Returns(new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseStatus = ResponseStatus.Completed,
                Content = JsonSerializer.Serialize(GetHostsVulnerabilities())  ,
                ContentType = "application/json",
                ContentLength = 2
            });
    }
    
    private static List<HostsService> GetHostsService()
    {
        var list = new List<HostsService> ()
        {
            new HostsService
            {
                Id = 1,
                HostId = 1,
                Name = "Service 1",
                Port = 443,
                Protocol = "tcp"
            },
            new HostsService
            {
                Id = 2,
                HostId = 1,
                Name = "Service 2",
                Port = 80,
                Protocol = "tcp"
            },
        };

        return list;

    }

    private static List<Vulnerability> GetHostsVulnerabilities()
    {
        var list = new List<Vulnerability>()
        {
            new ()
            {
                AnalystId = 1,
                Comments = "Vul 1",
                DetectionCount = 1,
                FirstDetection = DateTime.Now,
                FixTeamId = 1,
                HostId = 1,
                Id = 1,
                CvssBaseScore = 6
            },
            new ()
            {
                AnalystId = 1,
                Comments = "Vul 2",
                DetectionCount = 1,
                FirstDetection = DateTime.Now,
                FixTeamId = 1,
                HostId = 1,
                Id = 2,
                CvssBaseScore = 7
            }
        };
        return list;
    }
}