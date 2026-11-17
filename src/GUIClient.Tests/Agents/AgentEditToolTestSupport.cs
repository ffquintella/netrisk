using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Agents;
using GUIClient.Agents.EditTools;
using GUIClient.Agents.ReadTools;
using Model.DTO;
using Model.Entities;
using NSubstitute;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// One editable item of every kind, id 1, behind substituted ClientServices that record every write. The item text
/// carries what must never travel back to the agent (a vault reference, an injection), so a test can check the edit
/// result echoes none of it.
/// </summary>
internal sealed class AgentEditFixtures
{
    public Risk Risk { get; } = new()
    {
        Id = 1, Subject = "Ransomware on payroll", Status = "New", Notes = "Old notes " + AgentReadFixtures.Vault,
        ScenarioCause = "Phishing", LastUpdate = new DateTime(2026, 2, 1), SubmissionDate = new DateTime(2026, 1, 1)
    };

    public Vulnerability Finding { get; } = new()
    {
        Id = 1, Title = "OpenSSL " + AgentReadFixtures.Injection, Severity = "High", Status = 1,
        LifecycleStatus = FindingStatus.Active, FixTeamId = 2, LastDetection = new DateTime(2026, 3, 1)
    };

    public List<Team> Teams { get; } = new() { new Team { Value = 2, Name = "Blue" }, new Team { Value = 3, Name = "Red" } };

    public Incident Incident { get; } = new()
    {
        Id = 1, Name = "2026-7", Description = "Phishing wave", Impact = "Low", LastUpdate = new DateTime(2026, 4, 2)
    };

    public AssessmentRun Run { get; } = new() { Id = 1, AssessmentId = 1, EntityId = 1, Status = 1, Comments = "Old comment" };

    public Entity Entity { get; } = new()
    {
        Id = 1, DefinitionName = "application", Status = "Active", Updated = new DateTime(2026, 5, 1),
        EntitiesProperties = new List<EntitiesProperty>
        {
            new() { Id = 10, Type = "name", Name = "name-1", Value = "Payroll" },
            new() { Id = 11, Type = "description", Name = "description-1", Value = "Runs payroll" },
            new() { Id = 12, Type = "criticality", Name = "criticality-1", Value = "4" },
            new() { Id = 13, Type = "apiKey", Name = "apiKey-1", Value = "AKIA-literal-api-key" }
        }
    };

    public Entity Person { get; } = new()
    {
        Id = 3, DefinitionName = "person", Status = "Active",
        EntitiesProperties = new List<EntitiesProperty> { new() { Id = 30, Type = "name", Name = "name-3", Value = "Ana" } }
    };

    public Host Host { get; } = new() { Id = 1, HostName = "web01", Ip = "10.0.0.1", Source = "scan", Comment = "old", Os = "Linux" };

    /// <summary>The run's answers as the server holds them now; question 2 is answered, question 1 is not.</summary>
    public List<AssessmentRunsAnswer> RunAnswers { get; } = new() { new() { Id = 1, RunId = 1, QuestionId = 2, AnswerId = 3 } };

    public IRisksService Risks { get; } = Substitute.For<IRisksService>();
    public IVulnerabilitiesService Findings { get; } = Substitute.For<IVulnerabilitiesService>();
    public ITeamsService TeamsService { get; } = Substitute.For<ITeamsService>();
    public IIncidentsService Incidents { get; } = Substitute.For<IIncidentsService>();
    public IAssessmentsService Assessments { get; } = Substitute.For<IAssessmentsService>();
    public IEntitiesService Entities { get; } = Substitute.For<IEntitiesService>();
    public IHostsService Hosts { get; } = Substitute.For<IHostsService>();

    public AgentEditFixtures()
    {
        Risks.GetAllRisksAsync(Arg.Any<bool>()).Returns(_ => Task.FromResult(new List<Risk> { Risk }));
        Risks.RiskSubjectExists("Taken subject").Returns(true);

        Findings.GetOneAsync(1).Returns(_ => Task.FromResult(Finding));
        Findings.UpdateLifecycleStatusAsync(1, Arg.Any<FindingStatus>(), Arg.Any<string?>(), Arg.Any<int?>())
            .Returns(call => Task.FromResult(new Vulnerability { Id = 1, Title = Finding.Title, LifecycleStatus = call.ArgAt<FindingStatus>(1) }));
        Findings.AddActionAsync(1, Arg.Any<int>(), Arg.Any<NrAction>()).Returns(call => Task.FromResult(call.ArgAt<NrAction>(2)));
        TeamsService.GetAllAsync().Returns(_ => Task.FromResult(Teams));

        Incidents.GetAllAsync().Returns(_ => Task.FromResult(new List<Incident> { Incident }));
        Incidents.UpdateAsync(Arg.Any<Incident>()).Returns(call => Task.FromResult(call.Arg<Incident>()));

        Assessments.GetAssessmentRuns(1).Returns(_ => new List<AssessmentRun> { Run });
        Assessments.GetAssessmentQuestions(1).Returns(new List<AssessmentQuestion>
        {
            new() { Id = 1, AssessmentId = 1, Question = "Backups?" }, new() { Id = 2, AssessmentId = 1, Question = "MFA?" }
        });
        Assessments.GetAssessmentAnswers(1).Returns(new List<AssessmentAnswer>
        {
            new() { Id = 1, AssessmentId = 1, QuestionId = 1, Answer = "Yes" },
            new() { Id = 2, AssessmentId = 1, QuestionId = 1, Answer = "No" },
            new() { Id = 3, AssessmentId = 1, QuestionId = 2, Answer = "Yes" }
        });
        Assessments.GetAssessmentRunAnsers(1, 1).Returns(_ => RunAnswers);
        Assessments.CreateRunAnswer(1, Arg.Any<AssessmentRunsAnswerDto>()).Returns(call => call.ArgAt<AssessmentRunsAnswerDto>(1));
        // The run update "lands" — the read-back sees the new comments.
        Assessments.When(a => a.UpdateAssessmentRun(Arg.Any<AssessmentRunDto>()))
            .Do(call => Run.Comments = call.Arg<AssessmentRunDto>().Comments);

        Entities.GetAllFromServerAsync(Arg.Any<bool>()).Returns(_ => Task.FromResult(new List<Entity> { Entity, Person }));
        Entities.SaveEntityAsync(Arg.Any<EntityDto>()).Returns(_ => Task.FromResult<Entity?>(Entity));

        Hosts.GetFilteredAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => (new List<Host> { Host }, 1));
    }

    public AgentEditServices Services() =>
        new(Risks, Findings, TeamsService, Incidents, Assessments, Entities, Hosts);

    public IReadOnlyList<IAgentTool> Catalog() => AgentEditToolCatalog.Create(Services());

    /// <summary>A minimal valid change for every edit tool, with the version its read tool would hand out now.</summary>
    public JsonObject ArgumentsFor(string tool) => tool switch
    {
        "update_risk" => new JsonObject { ["risk_id"] = 1, ["version"] = AgentVersions.Risk(Risk), ["notes"] = "New notes" },
        "update_finding_status" => new JsonObject
        {
            ["finding_id"] = 1, ["version"] = AgentVersions.Finding(Finding), ["status"] = "verified",
            ["justification"] = "Reproduced on web01"
        },
        "update_finding_assignment" => new JsonObject
        {
            ["finding_id"] = 1, ["version"] = AgentVersions.Finding(Finding), ["fix_team_id"] = 3
        },
        "add_finding_note" => new JsonObject { ["finding_id"] = 1, ["note"] = "Patched in staging" },
        "update_incident" => new JsonObject
        {
            ["incident_id"] = 1, ["version"] = AgentVersions.Incident(Incident), ["cause"] = "Weak mail filter"
        },
        "answer_assessment_question" => new JsonObject
        {
            ["assessment_id"] = 1, ["run_id"] = 1, ["question_id"] = 1, ["answer_id"] = 2
        },
        "comment_assessment_run" => new JsonObject
        {
            ["assessment_id"] = 1, ["run_id"] = 1, ["version"] = AgentVersions.AssessmentRun(Run), ["comments"] = "Reviewed"
        },
        "update_entity_fields" => new JsonObject
        {
            ["entity_id"] = 1, ["version"] = AgentVersions.Entity(Entity), ["description"] = "Runs the monthly payroll"
        },
        "update_host_fields" => new JsonObject { ["host_id"] = 1, ["version"] = AgentVersions.Host(Host), ["comment"] = "DMZ" },
        _ => throw new Xunit.Sdk.XunitException($"No test arguments for edit tool '{tool}': add it to AgentEditFixtures.")
    };

    public static TheoryData<string> AllTools()
    {
        var data = new TheoryData<string>();
        foreach (var tool in new AgentEditFixtures().Catalog()) data.Add(tool.Name);
        return data;
    }
}

/// <summary>The whole reviewed surface over fixtures: the M62 read tools and the M63 edit tools.</summary>
internal static class AgentAllTools
{
    public static IReadOnlyList<IAgentTool> Catalog(AgentEditFixtures? edits = null) =>
        new AgentReadFixtures().Catalog().Concat((edits ?? new AgentEditFixtures()).Catalog()).ToList();

    /// <summary>Valid arguments for any reviewed tool.</summary>
    public static JsonObject ArgumentsFor(string tool, AgentEditFixtures? edits = null) =>
        AgentReadToolArguments.Names.Contains(tool)
            ? AgentReadToolArguments.For(tool)
            : (edits ?? new AgentEditFixtures()).ArgumentsFor(tool);
}
