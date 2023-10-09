using System.Text.RegularExpressions;

namespace Tools.Network;

public class FqdnTool
{
    public static bool IsValid(string fqdn)    
    {    
            
        string Pattern = @"(?=^.{1,254}$)(^(?:(?!\d+\.)[a-zA-Z0-9_\-]{1,63}\.?)+(?:[a-zA-Z]{1,})$)";    
  
        Regex check = new Regex(Pattern);    
  
        if (string.IsNullOrEmpty(fqdn)) return false;    
         
        //Matching the pattern    
        return check.IsMatch(fqdn, 0);    
    } 
}