using BetterGenshinImpact.Core.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.Helpers;

public static class HardwarePortDetector
{
    private static readonly Regex ComPortRegex = new(@"\(COM(?<port>\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ManualComPortRegex = new(@"^(?:\\\\\.\\)?(?:COM)?\s*(?<port>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VidPidRegex = new(@"VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] FerrumKeywords =
    [
        "ferrum",
        "kmbox",
        "km box",
        "ch340",
        "ch341",
        "ch343",
        "cp210",
        "silicon labs",
        "usb serial",
        "usb-enhanced-serial",
        "serial device",
        "uart",
        "com0com",
        "virtual serial port pair",
        "reserved interface"
    ];

    private static readonly string[] MakcuKeywords =
    [
        "makcu",
        "kmbox",
        "km box",
        "ch340",
        "ch341",
        "ch343",
        "cp210",
        "silicon labs",
        "usb serial",
        "usb-enhanced-serial",
        "serial device",
        "uart",
        "vid_1a86"
    ];

    private static readonly string[] MakxdKeywords =
    [
        "makxd",
        "makcu",
        "kmbox",
        "km box",
        "ch340",
        "ch341",
        "ch343",
        "cp210",
        "silicon labs",
        "usb serial",
        "usb-enhanced-serial",
        "serial device",
        "uart",
        "vid_1a86"
    ];

    private static readonly string[] CommonKeywords =
    [
        "kmbox",
        "km box",
        "usb serial",
        "serial device",
        "uart"
    ];

    public static string ResolvePort(string vendor)
    {
        var candidates = GetCandidates();
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        var scoredCandidates = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = ScoreCandidate(candidate, vendor)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.PortNumber)
            .ToList();

        var best = scoredCandidates.FirstOrDefault(x => x.Score > 0);
        if (best != null)
        {
            return best.Candidate.Port;
        }

        return scoredCandidates[0].Candidate.Port;
    }

    public static string ResolveVidPid(string portName)
    {
        var normalizedPortName = NormalizePortName(portName);
        if (string.IsNullOrWhiteSpace(normalizedPortName))
        {
            return "Unknown";
        }

        var candidate = GetCandidates()
            .FirstOrDefault(x => string.Equals(x.Port, normalizedPortName, StringComparison.OrdinalIgnoreCase));

        if (candidate == null)
        {
            return "Unknown";
        }

        var match = VidPidRegex.Match(candidate.SearchText);
        return match.Success
            ? $"VID_{match.Groups["vid"].Value.ToUpperInvariant()}&PID_{match.Groups["pid"].Value.ToUpperInvariant()}"
            : "Unknown";
    }

    public static string GetBaudRateText(string vendor)
    {
        if (string.Equals(vendor, HardwareInputConfigValues.Makcu, StringComparison.OrdinalIgnoreCase))
        {
            return "115200 / 4000000";
        }

        return "115200";
    }

    public static string NormalizePortName(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return string.Empty;
        }

        var trimmed = portName.Trim();
        var match = ManualComPortRegex.Match(trimmed);
        if (match.Success && int.TryParse(match.Groups["port"].Value, out var portNumber) && portNumber > 0)
        {
            return $"COM{portNumber}";
        }

        return trimmed.Replace(" ", string.Empty).ToUpperInvariant();
    }

    private static int ScoreCandidate(ComPortCandidate candidate, string vendor)
    {
        var score = 0;
        var text = candidate.SearchText;

        if (text.Contains("com"))
        {
            score += 1;
        }

        if (string.Equals(vendor, HardwareInputConfigValues.Ferrum, StringComparison.OrdinalIgnoreCase))
        {
            score += ScoreByKeywords(text, FerrumKeywords);
        }
        else if (string.Equals(vendor, HardwareInputConfigValues.Makcu, StringComparison.OrdinalIgnoreCase))
        {
            score += ScoreByKeywords(text, MakcuKeywords);
            score -= ScoreByNegativeKeywords(text, ["ferrum", "com0com", "virtual serial port pair", "reserved interface"]);
        }
        else if (string.Equals(vendor, HardwareInputConfigValues.Makxd, StringComparison.OrdinalIgnoreCase))
        {
            score += ScoreByKeywords(text, MakxdKeywords);
            score -= ScoreByNegativeKeywords(text, ["ferrum", "com0com", "virtual serial port pair", "reserved interface"]);
        }
        else
        {
            score += ScoreByKeywords(text, CommonKeywords);
        }

        return score;
    }

    private static int ScoreByKeywords(string text, IReadOnlyList<string> keywords)
    {
        var score = 0;
        foreach (var keyword in keywords)
        {
            if (!text.Contains(keyword))
            {
                continue;
            }

            score += keyword.Length switch
            {
                >= 7 => 30,
                >= 4 => 15,
                _ => 5
            };
        }

        return score;
    }

    private static int ScoreByNegativeKeywords(string text, IReadOnlyList<string> keywords)
    {
        var score = 0;
        foreach (var keyword in keywords)
        {
            if (!text.Contains(keyword))
            {
                continue;
            }

            score += keyword.Length switch
            {
                >= 10 => 40,
                >= 6 => 25,
                _ => 10
            };
        }

        return score;
    }

    private static List<ComPortCandidate> GetCandidates()
    {
        var results = new List<ComPortCandidate>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, Caption, Description, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%' OR Caption LIKE '%(COM%' OR Description LIKE '%(COM%'");

            foreach (ManagementObject item in searcher.Get())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                var caption = item["Caption"]?.ToString() ?? string.Empty;
                var description = item["Description"]?.ToString() ?? string.Empty;
                var pnpDeviceId = item["PNPDeviceID"]?.ToString() ?? string.Empty;
                var combined = $"{name} {caption} {description} {pnpDeviceId}".Trim();
                var match = ComPortRegex.Match(combined);
                if (!match.Success)
                {
                    continue;
                }

                var portNumber = int.TryParse(match.Groups["port"].Value, out var parsedPort) ? parsedPort : int.MaxValue;
                results.Add(new ComPortCandidate(
                    $"COM{portNumber}",
                    portNumber,
                    combined.ToLowerInvariant()));
            }
        }
        catch
        {
            return results;
        }

        return results
            .GroupBy(x => x.Port, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private sealed record ComPortCandidate(string Port, int PortNumber, string SearchText);
}
