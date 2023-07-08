using Model.Rest;

namespace Model.Registration;

public class RegistrationSolicitationResult
{
    private RequestResult result;

    public RequestResult Result
    {
        get => result;
        set => result = value;
    }

    public string? RequestID { get; set; }
}