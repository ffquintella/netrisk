using Model;

namespace GUIClient.Extensions;

static class IntStatusExtension
{
    public static string StatusString(this IntStatus status)
    {
        switch (status)
        {
                case IntStatus.Open:
                    return "Open";
                case IntStatus.Closed:
                    return "Closed";
                case IntStatus.Critical:
                    return "Critical";
                case IntStatus.New:
                    return "New";
                case IntStatus.Duplicated:
                    return "Duplicate";
                case IntStatus.Fixed:
                    return "Fixed";
                case IntStatus.Mitigated:
                    return "Mitigated";
                case IntStatus.Prioritized:
                    return "Prioritized";
                case IntStatus.Rejected:
                    return "Rejected";
                case IntStatus.Reopened:
                    return "Reopened";
                case IntStatus.AwaitingFix:
                    return "Awaiting Fix";
                case IntStatus.FixAvailable:
                    return "Fix Available";
                case IntStatus.ManagementReview:
                    return "Management Review";
                case IntStatus.MitigationPlanned:
                    return "Mitigation Planned";
                case IntStatus.NotRelevant:
                    return "Not Relevant";
                case IntStatus.UnderReview:
                    return "Under Review";
                case IntStatus.AwaitingInternalResponse:
                    return "Awaiting Internal Response";
                case IntStatus.AwaitingUserResponse:
                    return "Awaiting User Response";
                case IntStatus.AwaitingVendorResponse:
                    return "Awaiting Vendor Response";
                case IntStatus.FixInProgress:
                    return "Fix In Progress";
                case IntStatus.FixNotApplicable:
                    return "Fix Not Applicable";
                case IntStatus.FixNotAvailable:
                    return "Fix Not Available";
                case IntStatus.FixNotPossible:
                    return "Fix Not Possible";
                case IntStatus.FixNotRequired:
                    return "Fix Not Required";
                case IntStatus.SentForPatch:
                    return "Sent For Patch";
                case IntStatus.AwaitingFixVerification:
                    return "Awaiting Fix Verification";
                case IntStatus.Ok:
                    return "Ok";
                case IntStatus.Resolved:
                    return "Resolved";
                case IntStatus.Retired:
                    return "Retired";
                case IntStatus.AwaitingApproval:
                    return "Awaiting Approval";
                case IntStatus.Outdated:
                    return "Outdated";
                case IntStatus.FixVerified:
                    return "Fix Verified";
                case IntStatus.FixWithProblems:
                    return "Fix With Problems";
                case IntStatus.FixNotVerified:
                    return "Fix Not Verified";
                case IntStatus.FixNotTested:
                    return "Fix Not Tested";
                case IntStatus.Regular:
                    return "Regular";
                case IntStatus.Deleted:
                    return "Deleted";
                case IntStatus.NeedsMoreInfo:
                    return "Needs More Info";
                case IntStatus.NeedsFurtherInvestigation:
                    return "Needs Further Investigation";
                default:
                    return "Unrecognized status";
        }
    }
}
