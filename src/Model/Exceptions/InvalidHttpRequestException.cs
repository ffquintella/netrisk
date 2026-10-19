
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

    /// <summary>
    /// The stable <c>error</c> code of a structured refusal (<c>{ error, message }</c>), when the server sent one — the
    /// risk index API's <c>risk_index_in_shadow</c>, <c>org_requires_global_scope</c>, <c>derived_computation_busy</c>…
    /// (S40 §8.1). A screen translates it (<c>RiskApi_‹code›MSG</c>) and falls back to <see cref="Exception.Message"/>,
    /// which then holds the server's own sentence. Null for callers that do not read the body.
    /// </summary>
    public string? ErrorCode { get; init; }

    public InvalidHttpRequestException(string message, string url, string method) : base(message)
    {
        Url = url;
        Method = method;
    }
}
