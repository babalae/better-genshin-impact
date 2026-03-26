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

    private static readonly string[] FerrumKeywords =
    [
        "ferrum",
        "kmbox",
        "km box",
        "ch340",
        "ch341",
        "cp210",
        "silicon labs",
        "usb serial",
        "serial device",
        "uart"
    ];

    private static readonly string[] MakcuKeywords =
    [
        "makcu",
        "kmbox",
        "km box",
        "ch340",
        "ch341",
        "cp210",
        "silicon labs",
        "usb serial",
        "serial device",
        "uart"
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
