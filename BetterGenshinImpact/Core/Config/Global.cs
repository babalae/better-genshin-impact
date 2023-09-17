using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Config
{
    public class Global
    {
        public static string StartUpPath { get; private set; } = AppDomain.CurrentDomain.BaseDirectory;

        public static string Absolute(string relativePath)
        {
            return Path.Combine(StartUpPath, relativePath);
        }
    }
}
