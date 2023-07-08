namespace Model.Globalization;

public class LanguageModel
{
    private readonly string name;
    private readonly string nativeName;
    private readonly string code;

    public string Name => name;

    public string NativeName => nativeName;

    public string Code => code;

    public LanguageModel(string name, string nativeName, string code)
    {
        this.name = name;
        this.nativeName = nativeName;
        this.code = code;
    }
}