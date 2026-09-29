using DAL.Entities;
using DAL.Enums;
using Model.Integrations;
using Serilog;
using ServerServices.Integrations.IssueTrackers.Jira;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers;

/// <summary>
/// Jira Server / Data Center.
///
/// The whole request shape is inherited from <see cref="JiraIssueTrackerProvider"/>; what makes this
/// a separate provider rather than a flag is that <see cref="JiraDialect"/> reads a different REST
/// version, body format and credential shape off the kind. Everything below is either identity or a
/// sentence said when a request fails, and the sentences are the point: this provider exists because
/// pointing the Cloud provider at a Data Center instance produces a <b>403 on a valid credential</b>,
/// which is indistinguishable from a permission problem and sends the operator to rotate a token.
///
/// Of the 4.6 facet, Data Center gets Assets — served from the instance at <c>/rest/assets/1.0</c>
/// (or <c>/rest/insight/1.0</c> on Insight), see <c>JiraAssetsClient</c> — and not Service
/// Management, which stays Cloud-only and is refused with a reason by <c>JiraIntegrationService</c>.
/// </summary>
public class JiraDataCenterIssueTrackerProvider(ILogger logger, IOutboundHttpClient http)
    : JiraIssueTrackerProvider(logger, http)
{
    public override IssueTrackerProviderKind Kind => IssueTrackerProviderKind.JiraDataCenter;

    public override string Name => "Jira Data Center";

    public override IssueTrackerCapabilities Capabilities => new()
    {
        SupportsWebhooks = true,
        SupportsComments = true,
        SupportsTransitions = true,
        SupportsLabels = true,
        SupportsPriority = true,
        SetupHint = "Base URL is your instance root, including any context path "
                    + "(https://jira.acme.com, or https://acme.com/jira). Leave the authentication "
                    + "user empty and put a Personal Access Token in the credential — Data Center "
                    + "accepts a PAT only as a bearer token. Fill the authentication user only for "
                    + "username + password basic auth, which most instances now refuse. Jira "
                    + "webhooks carry no signature, so the receiver URL must include the connection's "
                    + "webhook secret as the ?secret= query parameter."
    };

    protected override ConnectionTestResult Describe(IssueTrackerConnection connection,
        OutboundHttpResponse response)
    {
        var credential = JiraDialect.UsesBearer(connection)
            ? "The credential is being sent as a bearer token, which is what a Personal Access Token "
              + "requires."
            : $"The credential is being sent as basic auth for user '{connection.AuthUser}'. If it is "
              + "a Personal Access Token, clear the authentication user — Data Center accepts a PAT "
              + "only as a bearer token, and rejects it as a basic-auth password.";

        return response.StatusCode switch
        {
            0 => ConnectionTestResult.Fail($"{Name} could not be reached: {response.TransportError}"),

            401 => ConnectionTestResult.Fail($"{Name} rejected the credentials (401). " + credential),

            // Seraph's CAPTCHA challenge is the reason this is not simply "check the permissions":
            // after a handful of failed logins it answers 403 for a correct credential too, and the
            // only way out is a browser sign-in that solves the CAPTCHA.
            403 => ConnectionTestResult.Fail(
                $"{Name} accepted the request's credentials and refused it (403). Either the account "
                + "lacks Browse Projects on the project, or the account is behind Jira's CAPTCHA "
                + "challenge after earlier failed logins — sign in to the instance once in a browser "
                + "to clear it. " + credential),

            404 => ConnectionTestResult.Fail(
                $"{Name} returned 404. Check the base URL, including the context path if the instance "
                + "is not at the domain root."),

            _ => ConnectionTestResult.Fail($"{Name} answered HTTP {response.StatusCode}.")
        };
    }
}
