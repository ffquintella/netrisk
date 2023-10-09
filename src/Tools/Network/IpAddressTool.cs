using System.Text.RegularExpressions;

namespace Tools.Network;

public static class IpAddressTool
{
    public static bool IsValid(string Address)    
    {    
        //Match pattern for IP address    
        string Pattern = @"^([1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5])(\.([0-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5])){3}$";    
        //Regular Expression object    
        Regex check = new Regex(Pattern);    
  
        //check to make sure an ip address was provided    
        if (string.IsNullOrEmpty(Address))    
            //returns false if IP is not provided    
            return false;    
         
        //Matching the pattern    
        return check.IsMatch(Address, 0);    
    } 
}