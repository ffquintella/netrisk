using JetBrains.Annotations;
using RestSharp;

namespace ClientServices.Tests.Mock;

public class ReadOnlyRestClientOptionsWrapper: ReadOnlyRestClientOptions
{
    public ReadOnlyRestClientOptionsWrapper() : base(new RestClientOptions("http://localhost:5000"))
    {
    }
}