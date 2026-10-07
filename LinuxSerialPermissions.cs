using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BitFab.KW1281Test;

internal static class LinuxSerialPermissions
{
    internal static IEnumerable<string> ForDevice(string portName)
    {
        if (!OperatingSystem.IsLinux()) return [];
        string? group = null;
        try
        {
            var mode = File.GetUnixFileMode(portName);
            if (mode.HasFlag(UnixFileMode.GroupRead | UnixFileMode.GroupWrite))
            {
                var start = new ProcessStartInfo("stat") { RedirectStandardOutput = true, UseShellExecute = false };
                foreach (string argument in new[] { "-Lc", "%G", "--", portName }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start);
                if (process != null)
                {
                    if (process.WaitForExit(2000) && process.ExitCode == 0) group = process.StandardOutput.ReadToEnd().Trim();
                    else if (!process.HasExited) process.Kill();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            // The permission explanation is still useful if device metadata is inaccessible.
        }
        return Describe(portName, group);
    }

    internal static IEnumerable<string> Describe(string portName, string? group)
    {
        yield return "Linux serial access requires read and write permission.";
        if (!string.IsNullOrWhiteSpace(group) && group != "root" && group.Any(char.IsLetter) &&
            group.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            yield return $"Add your user to the device's {group} group:";
            yield return $"  sudo usermod -aG {Quote(group)} \"$USER\"";
            yield return "Log out and log back in, then run the tool without sudo.";
        }
        else
        {
            yield return $"Check the device's permissions: ls -l -- {Quote(portName)}";
            yield return "For temporary access to this device:";
            yield return $"  sudo setfacl -m \"u:$USER:rw\" -- {Quote(portName)}";
            yield return "Temporary access must be granted again after reconnecting the cable. Run the tool without sudo.";
        }
        yield return "sudo can remove KW1281TEST_PROFILE, KW1281TEST_PORT and KW1281TEST_BAUD_RATE from the environment.";
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
