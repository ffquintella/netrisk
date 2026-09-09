using System.Text.Json;
using ClientServices.Interfaces;
using Model.Exceptions;
using Model.Rest;
using RestSharp;

namespace ClientServices.Services;

public class RestServiceBase(IRestService restService) : ServiceBase
{
    protected IRestService RestService { get; } = restService;

    /// <summary>
    /// The client a non-idempotent request must go through — deliberately the plain one, with no
    /// retry policy wrapped around it.
    ///
    /// <see cref="IRestService.GetReliableClient"/> retries any request that answers 500, 502, 503 or
    /// 504, and the wrapper it returns does so with no delay between attempts and swallowing every
    /// exception on the way, so one call can leave as a dozen. That is right for a GET and wrong for
    /// every POST, PUT, PATCH and DELETE in this assembly: the server has already done the work by
    /// the time it fails to say so, and doing it again is not reliability. One click on "Sync now"
    /// against an endpoint answering 5xx produced eleven Vision One synchronizations in three
    /// seconds; the same client sends the host and vulnerability creates, where a retry is a
    /// duplicate row rather than a duplicate job.
    ///
    /// Only the retry is dropped. <c>GetClient</c> is the same client underneath — same
    /// authentication, same <c>ThrowOnAnyError</c> — so a caller's status handling is unaffected.
    /// Read paths stay on the reliable client, which is where retrying belongs.
    /// </summary>
    protected IRestClient MutatingClient(bool reportErrorResponses = false)
        => RestService.GetClient(reportErrorResponses: reportErrorResponses);

    /// <summary>
    /// Case-insensitive, because the API serializes with MVC's web defaults — its problem documents
    /// name the fields <c>title</c>, <c>status</c> and <c>errors</c>, and a case-sensitive read
    /// matched none of them. Every field of every <see cref="OperationError"/> read here came back
    /// at its default, so a 400 that named the invalid field was reported as a refusal with an
    /// empty reason. The tests did not catch it because they serialize their fixture with the same
    /// default options this read used, producing a document the API never sends.
    /// </summary>
    private static readonly JsonSerializerOptions ErrorOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Reads the API's <see cref="OperationError"/> out of a failed response, or null when the body
    /// is empty or is not one.
    ///
    /// Callers must fail whether or not this returns something: a non-OK status means the write did
    /// not happen, and only some endpoints answer with a structured error. Deserializing straight
    /// into <see cref="ErrorSavingException"/> and throwing only on a non-null result is how several
    /// of these methods used to report a rejected write as a success.
    /// </summary>
    protected static OperationError? TryReadOperationError(RestResponse? response)
    {
        if (string.IsNullOrWhiteSpace(response?.Content)) return null;

        try
        {
            return JsonSerializer.Deserialize<OperationError>(response.Content, ErrorOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
