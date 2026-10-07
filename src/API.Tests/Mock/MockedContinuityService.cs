using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using DAL.Enums;
using Model.Continuity;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IContinuityService"/> for <c>ContinuityControllerTest</c> (Stage 9.3).
///
/// Its ids drive every branch of the controller's error mapping without a per-test double: entity
/// <see cref="NewBia"/> creates a BIA and <see cref="ExistingBia"/> replaces one; 400 is an invalid
/// parameter, 403 a scoped caller (<c>global_scope</c>), 404 missing, 409 a conflict and 422 not a BIA
/// subject.
/// </summary>
public static class MockedContinuityService
{
    public const int NewBia = 10;
    public const int ExistingBia = 11;
    public const int Invalid = 400;
    public const int Forbidden = 403;
    public const int Missing = 404;
    public const int Conflicting = 409;
    public const int NotSubject = 422;

    private static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Throws the domain exception an id stands for; any other id passes.</summary>
    private static void Gate(int id)
    {
        switch (id)
        {
            case Invalid: throw new InvalidParameterException("RtoMinutes", "A duration is a whole number of minutes.");
            case Forbidden: throw new PermissionInvalidException("global_scope", 1, "Continuity");
            case Missing: throw new DataNotFoundException("Entity", id.ToString());
            case Conflicting: throw new DataAlreadyExistsException("netrisk", "bia_dependencies", "1:2", "Already declared.");
            case NotSubject: throw new RuleBrokenException("Not a BIA subject.", "entity_not_bia_subject");
        }
    }

    public static BusinessImpactAnalysisDto Bia(int entityId) => new()
    {
        Id = 1, EntityId = entityId, MtpdMinutes = 480, RtoMinutes = 240, RpoMinutes = 60, AssessedAt = When,
        CreatedAt = When, CreatedById = 1
    };

    public static RestorationTestDto Test(int entityId) => new()
    {
        Id = 5, EntityId = entityId, TestedAt = When, Outcome = RestorationTestOutcome.Succeeded,
        AchievedRtoMinutes = 200, DeclaredRtoMinutes = 240, CreatedAt = When, RecordedById = 1
    };

    public static ContinuitySettingsDto Settings() => new() { RestorationTestValidityDays = 365, UnverifiedThreatWeight = 0.5m };

    public static IContinuityService Create()
    {
        var service = Substitute.For<IContinuityService>();

        service.GetSubjectsAsync().Returns(_ => Task.FromResult(new List<ContinuitySubjectDto>
        {
            new() { EntityId = NewBia, Name = "Enrolment", DefinitionName = "businessProcess", HasBia = true }
        }));

        service.GetProfileAsync(Arg.Any<int>(), Arg.Any<ClaimsPrincipal?>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            Gate(id);
            return new ContinuityProfileDto
            {
                Subject = new ContinuitySubjectDto { EntityId = id, Name = "Enrolment", HasBia = true },
                Bia = Bia(id),
                Threat = new ContinuityThreatDto { ThreatWeight = 0.5m, IsThreatened = true, UnverifiedWeight = 0.5m }
            };
        });

        service.SaveBiaAsync(Arg.Any<int>(), Arg.Any<BusinessImpactAnalysisRequest>(), Arg.Any<int?>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            Gate(id);
            return new BusinessImpactAnalysisWriteResult { Created = id != ExistingBia, Bia = Bia(id) };
        });

        service.DeleteBiaAsync(Arg.Any<int>()).Returns(call =>
        {
            Gate(call.ArgAt<int>(0));
            return Task.CompletedTask;
        });

        service.AddDependencyAsync(Arg.Any<int>(), Arg.Any<BiaDependencyCreateRequest>(), Arg.Any<int?>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            var request = call.ArgAt<BiaDependencyCreateRequest>(1);
            Gate(id);
            Gate(request.ProviderEntityId);
            return new BiaDependencyDto { Id = 3, DependentEntityId = id, ProviderEntityId = request.ProviderEntityId, CreatedAt = When };
        });

        service.DeleteDependencyAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            Gate(call.ArgAt<int>(0));
            Gate(call.ArgAt<int>(1));
            return Task.CompletedTask;
        });

        service.GetRestorationTestsAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            Gate(id);
            return new List<RestorationTestDto> { Test(id) };
        });

        service.RecordRestorationTestAsync(Arg.Any<int>(), Arg.Any<RestorationTestCreateRequest>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var id = call.ArgAt<int>(0);
                Gate(id);
                return Test(id);
            });

        service.VoidRestorationTestAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<RestorationTestVoidRequest>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var id = call.ArgAt<int>(0);
                Gate(id);
                Gate(call.ArgAt<int>(1));
                var test = Test(id);
                test.VoidedAt = When;
                test.VoidReason = call.ArgAt<RestorationTestVoidRequest>(2).Reason;
                return test;
            });

        service.GetRestorationVerificationMetricAsync().Returns(_ => Task.FromResult(new RestorationVerificationMetricDto
        {
            ComputedAt = When, ValidityDays = 365, UnverifiedWeight = 0.5m, ThreatenedCriticalProcessesWeighted = 1.5m
        }));

        service.GetSettingsAsync().Returns(_ => Task.FromResult(Settings()));

        service.SaveSettingsAsync(Arg.Any<ContinuitySettingsRequest>()).Returns(call =>
        {
            var request = call.ArgAt<ContinuitySettingsRequest>(0);
            if (request.RestorationTestValidityDays is not { } days || days == Invalid)
                throw new InvalidParameterException("continuity_restoration_test_validity_days", "Out of range.");
            if (days == Forbidden) throw new PermissionInvalidException("global_scope", 1, "Continuity.SaveSettings");

            return new ContinuitySettingsDto
            {
                RestorationTestValidityDays = days, UnverifiedThreatWeight = request.UnverifiedThreatWeight ?? 0.5m
            };
        });

        return service;
    }
}
