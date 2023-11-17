namespace Tools.Security;

public class PasswordTools
{
    public static  bool CheckPasswordComplexity(string? password)
    {

        if (string.IsNullOrEmpty(password)) return false;
        
        if (password.Length < 8 || password.Length > 64)
            return false;
        
        if (!password.Any(char.IsUpper))
            return false;
        
        if (!password.Any(char.IsLower))
            return false;
        
        if (password.Contains(" "))
            return false;
        
        string specialCh = @"%!@#$%^&*()?/>.<,:;'\|}]{[_~`+=-" + "\"";
        char[] specialChArray = specialCh.ToCharArray();
        foreach (char ch in specialChArray) {
            if (password.Contains(ch))
                return true;
        }

        return false;
    }
}