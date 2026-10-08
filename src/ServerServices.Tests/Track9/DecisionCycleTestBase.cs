using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DAL.Entities;
using DAL.Enums;
using Model.DecisionCycle;
using ServerServices.Governance;

namespace ServerServices.Tests.Track9;

/// <summary>
/// The organisation every Stage 9.9 service test starts from (S50 §8): the Stage 9.1 one (<see cref="RiskChainTestBase"/>:
/// units 100 and 200, analyst 7), plus
/// <code>
///   1 cro       administrator
///   2 owner     submits, owns and manages the test risks
///   3, 4, 5     committee members
///   9 auditor   the third line — and an administrator too, to show the flag does not lift the rule
/// </code>
/// with the seeded <c>ThirdLineAuditor</c> role and its permission, the segregation switch on, and the closure reason the
/// archive uses.
/// </summary>
public abstract class DecisionCycleTestBase : RiskChainTestBase
{
    protected const int Cro = 1;
    protected const int Owner = 2;
    protected const int MemberA = 3;
    protected const int MemberB = 4;
    protected const int MemberC = 5;
    protected const int Auditor = 9;
    protected const int ThirdLineRole = 50;
    protected const int NotRelevant = 4;

    protected DecisionCycleTestBase()
    {
        SeedUnscoped(ctx =>
        {
            var marker = new Permission
            {
                Id = 900, Key = ThirdLineAssurance.PermissionKey, Name = "Third line", Description = "Read-only", Order = 1
            };
            ctx.Permissions.Add(marker);
            ctx.Roles.Add(new Role { Value = ThirdLineRole, Name = ThirdLineAssurance.RoleName, Permissions = { marker } });

            ctx.Users.Add(NewUser(Cro, "cro", admin: true));
            ctx.Users.Add(NewUser(Owner, "owner"));
            ctx.Users.Add(NewUser(MemberA, "member-a"));
            ctx.Users.Add(NewUser(MemberB, "member-b"));
            ctx.Users.Add(NewUser(MemberC, "member-c"));
            ctx.Users.Add(NewUser(Auditor, "auditor", admin: true, roleId: ThirdLineRole));

            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.SegregationSetting, Value = "true" });
            ctx.CloseReasons.Add(new CloseReason { Value = NotRelevant, Name = "Not relevant" });
        });
    }

    protected static User NewUser(int id, string name, bool admin = false, int roleId = 0) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin, RoleId = roleId, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@example.test"
    };

    /// <summary>
    /// A risk owned, managed and submitted by <see cref="Owner"/>, in <paramref name="status"/>, with a management review
    /// that settled it — so the Track 8 state machine lets it close.
    /// </summary>
    protected void ReviewedRisk(int id, int? unit = UnitA, string status = "Mgmt Reviewed", float score = 5f,
        DateTime? submittedAt = null)
    {
        AddRisk(id, unit, status, score);
        SeedUnscoped(ctx =>
        {
            var risk = ctx.Risks.Single(r => r.Id == id);
            risk.Owner = Owner;
            risk.Manager = Owner;
            risk.SubmittedBy = Owner;
            if (submittedAt is { } at) risk.SubmissionDate = at;

            ctx.MgmtReviews.Add(new MgmtReview
            {
                Id = 10_000 + id, RiskId = id, SubmissionDate = DateTime.UtcNow.AddDays(-1), Review = 1, Reviewer = Cro,
                NextStep = 2, Comments = "Not worth treating.", NextReview = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90))
            });
        });
    }

    protected static RiskArchiveRequest ArchiveRequest(params ReassessmentTriggerType[] conditions) => new()
    {
        Justification = "Legacy kiosk, isolated network, decommissioning planned.",
        CloseReason = NotRelevant,
        Conditions = (conditions.Length == 0 ? [ReassessmentTriggerType.NewRegulation] : conditions)
            .Select(c => new RiskArchiveConditionRequest { TriggerType = c, Description = $"If {c} happens." })
            .ToList()
    };

    protected Risk RiskRow(int id) => Read(ctx => ctx.Risks.Single(r => r.Id == id));

    protected List<RiskArchive> ArchivesOf(int riskId) =>
        Read(ctx => ctx.RiskArchives.Where(a => a.RiskId == riskId).OrderBy(a => a.Id).ToList());

    protected List<AuditLog> Audit(string type) =>
        Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == type).OrderBy(a => a.Id).ToList());
}
