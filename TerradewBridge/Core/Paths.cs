using System;
using System.IO;

namespace Terradew.Core;

public static class Paths
{
    /// <summary>
    /// The folder both games share. Stardew's runtime maps ApplicationData to ~/.config on macOS,
    /// tModLoader's maps it to ~/Library/Application Support, so macOS uses ~/.config explicitly.
    /// </summary>
    public static string Root
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "Terradew");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Terradew");
        }
    }
}
