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
            return JsonSerializer.Deserialize<OperationError>(response.Content);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
