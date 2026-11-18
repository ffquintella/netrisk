using System.Collections.Generic;
using System.Threading;
using DAL.Enums;
using Model.Integrations;
using NSubstitute;
using ServerServices.Integrations.EntityMappings;

namespace API.Tests.Mock;

/// <summary>
/// The vendor → entity rules' double (T263, S40 §12.3): connection 1 lists one rule, id 1 updates and deletes, and the value
/// or id chooses the refusal — <c>dup</c>/409, <c>scoped</c>/403, id 404/404, <c>bad</c>/400 — so every branch of the
/// controller's mapping is reachable without a per-test double.
/// </summary>
public static class MockedVendorEntityMappingService
{
    public static VendorEntityMappingDto Rule(int id = 1, string value = "BU: Law") =>
        new(id, (int)IntegrationKind.TrendMicroVisionOne, 1, VendorMatchKind.V1CustomTag, value, null, 8, "Law School", 1,
            RestampPolicy.OnlyUnassigned, true);

    private static void Gate(VendorEntityMappingUpsert request)
    {
        switch (request.Value)
        {
            case "dup": throw VendorEntityMappingException.Conflict(VendorEntityMappingErrors.Duplicate, "duplicate");
            case "scoped": throw VendorEntityMappingException.Forbidden(VendorEntityMappingErrors.RequiresGlobalScope, "scope");
            case "bad": throw VendorEntityMappingException.BadRequest(VendorEntityMappingErrors.ValueRequired, "bad");
            case "boom": throw new System.InvalidOperationException("boom");
        }
    }

    public static IVendorEntityMappingService Create()
    {
        var service = Substitute.For<IVendorEntityMappingService>();
        service.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<int>(1) == 1 ? new List<VendorEntityMappingDto> { Rule() } : []);
        service.GetCandidatesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<VendorMappingCandidateDto> { new(VendorMatchKind.V1CustomTag, "BU: Law", null, 412) });
        service.CreateAsync(Arg.Any<VendorEntityMappingUpsert>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<VendorEntityMappingUpsert>(0));
                return Rule(5, call.ArgAt<VendorEntityMappingUpsert>(0).Value);
            });
        service.UpdateAsync(Arg.Any<int>(), Arg.Any<VendorEntityMappingUpsert>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<int>(0) == 404)
                    throw VendorEntityMappingException.NotFound(VendorEntityMappingErrors.NotFound, "gone");
                Gate(call.ArgAt<VendorEntityMappingUpsert>(1));
                return Rule(call.ArgAt<int>(0), call.ArgAt<VendorEntityMappingUpsert>(1).Value);
            });
        service.DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<int>(0) == 404
                ? throw VendorEntityMappingException.NotFound(VendorEntityMappingErrors.NotFound, "gone")
                : System.Threading.Tasks.Task.CompletedTask);
        service.PreviewAsync(Arg.Any<VendorEntityMappingUpsert>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Gate(call.ArgAt<VendorEntityMappingUpsert>(0));
                return new VendorEntityMappingPreviewDto(412, 37, 12, 3, 360, []);
            });
        return service;
    }
}
