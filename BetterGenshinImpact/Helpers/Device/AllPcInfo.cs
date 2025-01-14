using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Helpers.Device;

[Serializable]
public class AllPcInfo
{
    public PCInfo? WMI { get; set; }

    public object? DxDiag { get; set; }
    
    public static string GetJson()
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(GetPcInfo());
    }
    
    public static AllPcInfo GetPcInfo()
    {
        var pcInfo = new AllPcInfo();
        pcInfo.WMI = GetPCInfo.GetClass();
        pcInfo.DxDiag = GetDxDiagInfo();
        return pcInfo;
    }


    static object? GetDxDiagInfo()
    {
        try
        {
            var path = Global.Absolute("User\\dxdiag.txt");
            
            if (!File.Exists(path))
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "dxdiag",
                    Arguments = $"/t \"{path}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process? process = Process.Start(psi);
                process?.WaitForExit();

                if (process?.ExitCode != 0)
                {
                    Debug.WriteLine("DxDiag命令执行失败。");
                    TaskControl.Logger.LogDebug("DxDiag命令执行失败。" + process?.ExitCode);
                    return null;
                }
            }

            
            // Read the input file
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            string input = File.ReadAllText(path, Encoding.GetEncoding("GB2312"));

            // Parse the content
            var parsed = Parse(input);
            Debug.WriteLine("DxDiag命令执行成功。");
            return parsed;
        }
        catch (Exception e)
        {
            Debug.WriteLine("DxDiag命令执行失败：" + e.Message);
            TaskControl.Logger.LogDebug("DxDiag命令执行失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }
        return null;
    }

    public static Dictionary<string, object> Parse(string input)
    {
        var result = new Dictionary<string, object>();
        var currentSection = "";
        var currentData = new Dictionary<string, object>();
        var keyCount = new Dictionary<string, int>();


        string[] lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Check if it's a section header
            if (line.StartsWith("----"))
            {
                // Save previous section if exists
                if (!string.IsNullOrEmpty(currentSection) && currentData.Count > 0)
                {
                    result[currentSection] = currentData;
                    currentData = new Dictionary<string, object>();
                }

                // Get next line as section name
                continue;
            }

            // If line contains only letters and spaces, it's a section name
            if (Regex.IsMatch(line.Trim(), @"^[A-Za-z ]+$"))
            {
                currentSection = line.Trim();
                continue;
            }

            // Parse key-value pairs
            if (line.Contains(":"))
            {
                var colonIndex = line.IndexOf(':');
                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();

                // Add to current section data
                if (!string.IsNullOrEmpty(key))
                {
                    if (currentData.ContainsKey(key))
                    {
                        if (!keyCount.ContainsKey(key))
                        {
                            keyCount[key] = 1;
                        }
                        keyCount[key]++;
                        key = $"{key}_{keyCount[key]}";
                    }
                    else
                    {
                        keyCount[key] = 1;
                    }

                    currentData[key] = value;
                }
            }
        }

        // Add last section
        if (!string.IsNullOrEmpty(currentSection) && currentData.Count > 0)
        {
            result[currentSection] = currentData;
        }

        return result;
    }
}