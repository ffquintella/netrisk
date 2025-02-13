﻿using System.Reflection;
using PdfSharp.Fonts;

namespace ServerServices.Helpers;

public class FontResolver : IFontResolver
{
    public byte[] GetFont(string faceName)
    {
        string assemblyDirectory = Path.GetDirectoryName(Assembly.GetAssembly(typeof(FontResolver))!.Location)!;
        
        //string runningDirectory = System.IO.Directory.GetCurrentDirectory();
        var fontsDir = Path.Combine(assemblyDirectory, "Fonts");
        using var stream = File.OpenRead(Path.Combine(fontsDir, faceName));
        var bytes = new byte[stream.Length];

        stream.ReadExactly(bytes);
        //stream.Read(bytes, 0, bytes.Length);
            
            
        return bytes;
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (familyName.Equals("Arial", StringComparison.CurrentCultureIgnoreCase))
        {
            if (isBold)
            {
                if (isItalic)
                    return new FontResolverInfo("Arial-Unicode-Bold-Italic.ttf");
                else
                    return new FontResolverInfo("Arial-Unicode-Bold.ttf");
            }
            else
            {
                if (isItalic)
                    return new FontResolverInfo("Arial-Unicode-Italic.ttf");
                else
                    return new FontResolverInfo("Arial-Unicode-Regular.ttf");
            }
        }
        if (familyName.Equals("Courier New", StringComparison.CurrentCultureIgnoreCase))
        {
            return new FontResolverInfo("CourierNew.ttf");
        }
        
        return new FontResolverInfo("Arial-Unicode-Regular.ttf");
    }
}