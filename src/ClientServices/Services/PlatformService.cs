﻿using System.Runtime.InteropServices;
using ClientServices.Interfaces;
using Model;

namespace ClientServices.Services;

public class PlatformService: IPlatformService
{
    public Platform GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Platform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Platform.MacOs;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Platform.Windows;
        }

        return Platform.Unknown;
    }
}