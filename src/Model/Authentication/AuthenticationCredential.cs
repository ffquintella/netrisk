namespace Model.Authentication;

public class AuthenticationCredential
{

    private string? samlCookie;
    private AuthenticationType authenticationType;


    public string? JWTToken { get; set; }
    
    public string? SAMLCookie
    {
        get => samlCookie;
        set => samlCookie = value;
    }

    public AuthenticationType AuthenticationType
    {
        get => authenticationType;
        set => authenticationType = value;
    }
}