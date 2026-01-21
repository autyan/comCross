using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;

namespace ComCross.Shell.Services;

public static class SerialPortScanHelper
{
    public static IReadOnlyList<string> GetPorts(string? scanPatternsCsv)
    {
        var ports = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var p in SerialPort.GetPortNames())
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    ports.Add(p);
                }
            }
        }
        catch
        {
            // best-effort
        }

        foreach (var pattern in ParsePatterns(scanPatternsCsv))
        {
            foreach (var p in Scan(pattern))
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    ports.Add(p);
                }
            }
        }

        return ports.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> ParsePatterns(string? patternsCsv)
    {
        if (string.IsNullOrWhiteSpace(patternsCsv))
        {
            yield break;
        }

        var parts = patternsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                yield return part;
            }
        }
    }

    private static IEnumerable<string> Scan(string pattern)
    {
        try
        {
            // If user provided a concrete path
            if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                return File.Exists(pattern) ? new[] { pattern } : Array.Empty<string>();
            }

            var lastSlash = pattern.LastIndexOf('/');
            if (lastSlash < 0)
            {
                // Only handle absolute unix-like paths for now.
                return Array.Empty<string>();
            }

            var directory = pattern.Substring(0, lastSlash);
            var filePattern = pattern.Substring(lastSlash + 1);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filePattern))
            {
                return Array.Empty<string>();
            }

            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            // Directory.GetFiles supports '*' and '?' in the file pattern.
            return Directory.GetFiles(directory, filePattern)
                .Where(File.Exists)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
