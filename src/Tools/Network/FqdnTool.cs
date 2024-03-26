using System.Text.RegularExpressions;

namespace Tools.Network;

public class FqdnTool
{
    public static bool IsValid(string fqdn)    
    {    

        string Pattern = @"^(?i)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$";
  
        Regex check = new Regex(Pattern);    
  
        if (string.IsNullOrEmpty(fqdn)) return false;    
         
        //Matching the pattern    
        return check.IsMatch(fqdn, 0);    
    } 
}