using System;
using System.Globalization;
using System.Linq;

namespace BitFab.KW1281Test;

internal static class ConnectionArguments
{
    internal const string PortVariable = "KW1281TEST_PORT";
    internal const string BaudVariable = "KW1281TEST_BAUD_RATE";

    // Normalize only the connection prefix. Existing command-specific argument parsing stays intact.
    internal static string[] Normalize(string[] args, VehicleProfile? profile, string? port, string? baud)
    {
        port = string.IsNullOrWhiteSpace(port) ? null : port;
        baud = string.IsNullOrWhiteSpace(baud) ? null : baud;
        if (args.Length >= 4 && (args[1].Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))) return args;
        int commandIndex = Enumerable.Range(1, Math.Min(3, Math.Max(0, args.Length - 1)))
            .FirstOrDefault(i => IsCommand(args[i]), -1);
        if (commandIndex == 3) return args;
        if (commandIndex == -1) return args; // Preserve legacy usage handling for unknown commands.
        string DefaultBaud() => baud ?? (profile != null ? "auto" : throw new ArgumentException($"Set {BaudVariable} or specify the baud rate."));
        string address = args[commandIndex - 1];
        string chosenPort;
        string chosenBaud;
        if (commandIndex == 1)
        {
            chosenPort = port ?? throw new ArgumentException($"Set {PortVariable} or specify the port.");
            chosenBaud = DefaultBaud();
        }
        else if (args[0].Equals("auto", StringComparison.OrdinalIgnoreCase) || int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            chosenPort = port ?? throw new ArgumentException($"Set {PortVariable} or specify the port.");
            chosenBaud = args[0];
        }
        else
        {
            chosenPort = args[0];
            chosenBaud = DefaultBaud();
        }
        return new[] { chosenPort, chosenBaud, address }.Concat(args.Skip(commandIndex)).ToArray();
    }

    private static bool IsCommand(string value) => value.ToLowerInvariant() is
        "actuatortest" or "adaptationread" or "adaptationsave" or "adaptationtest" or "autoscan" or
        "basicsetting" or "clarionvwpremium4safecode" or "clearcrashdata" or "clearfaultcodes" or "delcovwpremium5safecode" or
        "dumpccmrom" or "dumpclusternecrom" or "dumpedc15eeprom" or "dumpeeprom" or "dumpmarellimem" or "dumpmem" or "dumpram" or
        "dumprbxmem" or "dumprbxmemodd" or "dumprom" or "findlogins" or "getclusterid" or "getskc" or
        "groupread" or "loadeeprom" or "mapeeprom" or "readeeprom" or "readfaultcodes" or "readident" or
        "readram" or "readrom" or "readsoftwareversion" or "reset" or "setsoftwarecoding" or "togglerb4mode" or
        "writeedc15eeprom" or "writeeeprom" or "writeram" or "sensors";
}
