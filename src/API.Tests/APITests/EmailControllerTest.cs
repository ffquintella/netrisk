using System.Collections.Generic;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.DI;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Model.DTO;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Every collaborator is substituted, so no mail ever reaches a transport. The shared
/// <c>IUsersService</c> mock is configured rather than replaced — the base controller's
/// <c>GetUser()</c> depends on the account it already knows about.
/// </summary>
[TestSubject(typeof(EmailController))]
public class EmailControllerTest : BaseControllerTest
{
    private const int SingleFixRequest = 1;
    private const int TeamFixRequest = 2;
    private const int TeamFixRequestWithoutTeam = 3;
    private const int SingleFixRequestWithoutDestination = 4;
    private const int BrokenFixRequest = 999;

    private const int VulnerabilityWithHost = 10;
    private const int VulnerabilityWithoutHost = 11;
    private const int BrokenVulnerability = 12;

    private const int TeamId = 5;
    private const int EveryoneTeamId = 6;

    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly ITeamsService _teamsService = Substitute.For<ITeamsService>();
    private readonly IFixRequestsService _fixRequestsService = Substitute.For<IFixRequestsService>();
    private readonly IVulnerabilitiesService _vulnerabilitiesService = Substitute.For<IVulnerabilitiesService>();
    private readonly ICommentsService _commentsService = Substitute.For<ICommentsService>();
    private readonly ILocalizationService _localizationService = Substitute.For<ILocalizationService>();
    private readonly IUsersService _usersService;
    private readonly EmailController _controller;

    public EmailControllerTest()
    {
        ArrangeLocalization();
        ArrangeFixRequests();
        ArrangeVulnerabilities();
        ArrangeTeams();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["website:protocol"] = "https",
                ["website:host"] = "netrisk.test",
                ["website:port"] = "8443"
            })
            .Build();

        var provider = ServiceRegistration.GetServiceProvider(services =>
        {
            services.AddSingleton(_emailService);
            services.AddSingleton(_teamsService);
            services.AddSingleton(_fixRequestsService);
            services.AddSingleton(_vulnerabilitiesService);
            services.AddSingleton(_commentsService);
            services.AddSingleton(_localizationService);
            services.AddSingleton(configuration);
        });

        _usersService = provider.GetRequiredService<IUsersService>();
        ArrangeTeamMembership();

        _controller = provider.GetRequiredService<EmailController>();
    }

    /// <summary>
    /// The controller feeds the localizer's result straight into the mail subject, so it has to
    /// return a real <see cref="LocalizedString"/> and not the substitute default of null.
    /// </summary>
    private void ArrangeLocalization()
    {
        var localizer = Substitute.For<IStringLocalizer>();
        localizer[Arg.Any<string>()].Returns(new LocalizedString("subject", "subject"));

        _localizationService.GetLocalizer().Returns(localizer);
    }

    private void ArrangeFixRequests()
    {
        _fixRequestsService.GetByIdAsync(SingleFixRequest).Returns(new FixRequest
        {
            Id = SingleFixRequest,
            VulnerabilityId = VulnerabilityWithHost,
            Identifier = "single-identifier",
            IsTeamFix = false,
            SingleFixDestination = "dev@test.com"
        });

        _fixRequestsService.GetByIdAsync(TeamFixRequest).Returns(new FixRequest
        {
            Id = TeamFixRequest,
            VulnerabilityId = VulnerabilityWithHost,
            Identifier = "team-identifier",
            IsTeamFix = true,
            FixTeamId = TeamId
        });

        _fixRequestsService.GetByIdAsync(TeamFixRequestWithoutTeam).Returns(new FixRequest
        {
            Id = TeamFixRequestWithoutTeam,
            VulnerabilityId = VulnerabilityWithHost,
            Identifier = "team-without-team",
            IsTeamFix = true,
            FixTeamId = null
        });

        _fixRequestsService.GetByIdAsync(SingleFixRequestWithoutDestination).Returns(new FixRequest
        {
            Id = SingleFixRequestWithoutDestination,
            VulnerabilityId = VulnerabilityWithHost,
            Identifier = "single-without-destination",
            IsTeamFix = false,
            SingleFixDestination = null
        });

        _fixRequestsService.GetByIdAsync(BrokenFixRequest)
            .Returns<Task<FixRequest>>(_ => throw new DataNotFoundException("fixRequest", "999"));
    }

    private void ArrangeVulnerabilities()
    {
        _vulnerabilitiesService.GetById(VulnerabilityWithHost, true).Returns(new Vulnerability
        {
            Id = VulnerabilityWithHost,
            Title = "Outdated TLS",
            Description = "TLS 1.0 is enabled",
            Solution = "Disable TLS 1.0",
            Score = 7.5,
            Host = new Host { Id = 1, HostName = "srv-01", Source = "test" }
        });

        _vulnerabilitiesService.GetById(VulnerabilityWithoutHost, true).Returns(new Vulnerability
        {
            Id = VulnerabilityWithoutHost,
            Title = "Missing header",
            Description = null,
            Solution = null,
            Score = null,
            Host = null
        });

        _vulnerabilitiesService.GetById(BrokenVulnerability, true)
            .Returns<Vulnerability>(_ => throw new DataNotFoundException("vulnerability", "12"));
    }

    private void ArrangeTeams()
    {
        _teamsService.GetById(TeamId).Returns(new Team { Value = TeamId, Name = "Infrastructure" });
        _teamsService.GetById(EveryoneTeamId).Returns(new Team { Value = EveryoneTeamId, Name = "All" });
    }

    private void ArrangeTeamMembership()
    {
        var teamMembers = new List<User>
        {
            new User { Value = 2, Name = "first", Email = "first@test.com" },
            new User { Value = 3, Name = "second", Email = "second@test.com" }
        };

        var everyone = new List<User>
        {
            new User { Value = 2, Name = "first", Email = "first@test.com" },
            new User { Value = 3, Name = "second", Email = "second@test.com" },
            new User { Value = 4, Name = "third", Email = "third@test.com" }
        };

        _usersService.GetByTeamIdAsync(TeamId).Returns(teamMembers);
        _usersService.GetAllAsync().Returns(everyone);
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailToASingleDestination()
    {
        var result = await _controller.SendVulnerabilityUpdateMail(SingleFixRequest, "Patch scheduled");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("ok", ok.Value);

        await _emailService.Received(1).SendEmailAsync(
            "dev@test.com",
            Arg.Any<string>(),
            "VulnerabilityUpdate",
            "en",
            Arg.Any<object>());
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailToEveryTeamMember()
    {
        var result = await _controller.SendVulnerabilityUpdateMail(TeamFixRequest, "Patch scheduled");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("ok", ok.Value);

        await _emailService.Received(1).SendEmailAsync(
            "first@test.com", Arg.Any<string>(), "VulnerabilityUpdate", "en", Arg.Any<object>());
        await _emailService.Received(1).SendEmailAsync(
            "second@test.com", Arg.Any<string>(), "VulnerabilityUpdate", "en", Arg.Any<object>());
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailRejectsATeamFixWithoutATeam()
    {
        var result = await _controller.SendVulnerabilityUpdateMail(TeamFixRequestWithoutTeam, "Patch scheduled");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("FixTeamId is required for group fix request", badRequest.Value);

        await _emailService.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailRejectsASingleFixWithoutADestination()
    {
        var result = await _controller.SendVulnerabilityUpdateMail(
            SingleFixRequestWithoutDestination, "Patch scheduled");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("SingleFixDestination is required for single fix request update email", badRequest.Value);
    }

    [Fact]
    public async Task TestSendVulnerabilityUpdateMailReturnsServerErrorWhenTheFixRequestCannotBeRead()
    {
        var result = await _controller.SendVulnerabilityUpdateMail(BrokenFixRequest, "Patch scheduled");

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailToASingleDestination()
    {
        var fixRequest = new FixRequestDto
        {
            VulnerabilityId = VulnerabilityWithHost,
            Destination = "dev@test.com",
            Identifier = "single-identifier"
        };

        var result = await _controller.SendVulnerabilityFixRequestMail(fixRequest);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("ok", ok.Value);

        await _emailService.Received(1).SendEmailAsync(
            "dev@test.com",
            Arg.Any<string>(),
            "VulnerabilityFound",
            "en",
            Arg.Any<object>());
    }

    /// <summary>A vulnerability with no host and no score must still produce a mail.</summary>
    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailForAVulnerabilityWithoutHostOrScore()
    {
        var fixRequest = new FixRequestDto
        {
            VulnerabilityId = VulnerabilityWithoutHost,
            Destination = "dev@test.com",
            Identifier = "no-host-identifier"
        };

        var result = await _controller.SendVulnerabilityFixRequestMail(fixRequest);

        Assert.IsType<OkObjectResult>(result.Result);

        await _emailService.Received(1).SendEmailAsync(
            "dev@test.com", Arg.Any<string>(), "VulnerabilityFound", "en", Arg.Any<object>());
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailToATeam()
    {
        var fixRequest = new FixRequestDto
        {
            VulnerabilityId = VulnerabilityWithHost,
            FixTeamId = TeamId,
            Identifier = "team-identifier"
        };

        var result = await _controller.SendVulnerabilityFixRequestMail(fixRequest, sendToGroup: true);

        Assert.IsType<OkObjectResult>(result.Result);

        await _emailService.Received(1).SendEmailAsync(
            "first@test.com", Arg.Any<string>(), "VulnerabilityFound", "en", Arg.Any<object>());
        await _emailService.Received(1).SendEmailAsync(
            "second@test.com", Arg.Any<string>(), "VulnerabilityFound", "en", Arg.Any<object>());
    }

    /// <summary>The team named "all" fans the mail out to every user instead of the team's members.</summary>
    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailToTheAllTeamReachesEveryUser()
    {
        var fixRequest = new FixRequestDto
        {
            VulnerabilityId = VulnerabilityWithHost,
            FixTeamId = EveryoneTeamId,
            Identifier = "everyone-identifier"
        };

        var result = await _controller.SendVulnerabilityFixRequestMail(fixRequest, sendToGroup: true);

        Assert.IsType<OkObjectResult>(result.Result);

        await _emailService.Received(3).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), "VulnerabilityFound", "en", Arg.Any<object>());
        await _usersService.Received(1).GetAllAsync();
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailRejectsAGroupRequestWithoutATeam()
    {
        var fixRequest = new FixRequestDto
        {
            VulnerabilityId = VulnerabilityWithHost,
            FixTeamId = null,
            Identifier = "no-team-identifier"
        };

        var result = await _controller.SendVulnerabilityFixRequestMail(fixRequest, sendToGroup: true);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("FixTeamId is required for group fix request", badRequest.Value);
    }

    [Fact]
    public async Task TestSendVulnerabilityFixRequestMailReturnsServerErrorWhenTheVulnerabilityCannotBeRead()
    {
        var fixRequest = new FixRequestDto
        {
            VulnerabilityId = BrokenVulnerability,
            Destination = "dev@test.com",
            Identifier = "broken-identifier"
        };

        var result = await _controller.SendVulnerabilityFixRequestMail(fixRequest);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);

        await _emailService.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>());
    }
}
