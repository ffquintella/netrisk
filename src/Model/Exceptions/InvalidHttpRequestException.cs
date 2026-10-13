
using System;
using System.Net;

namespace Model.Exceptions;

public class InvalidHttpRequestException: Exception
{
    public String Url { get; set; }
    public String Method { get; set; }

    /// <summary>
    /// The status the server answered with, when the caller kept it. Null for the requests that never reached a server
    /// and for the older callers that do not set it. It is what lets a screen tell a refusal (403 — the third line is
    /// read-only, administrators included) from a failure without parsing the message.
    /// </summary>
    public HttpStatusCode? StatusCode { get; init; }

    public InvalidHttpRequestException(string message, string url, string method) : base(message)
    {
        Url = url;
        Method = method;
    }
}
