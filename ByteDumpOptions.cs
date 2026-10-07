using System;
using System.Collections.Generic;

namespace BitFab.KW1281Test;

internal static class ByteDumpOptions
{
    internal const string EnvironmentVariable = "KW1281TEST_DUMP";

    internal static (string[] Arguments, string? Path) Extract(string[] args, string? environment)
    {
        string? path = null;
        var remaining = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("--dump", StringComparison.OrdinalIgnoreCase))
            {
                remaining.Add(args[i]);
                continue;
            }
            if (path != null) throw new ArgumentException("Specify --dump only once.");
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("--dump requires a file path.");
            path = args[i];
        }
        return (remaining.ToArray(), path ?? (string.IsNullOrWhiteSpace(environment) ? null : environment));
    }
}
