using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Vision.Recognition
{
    /// <summary>
    /// Vision 上下文
    /// </summary>
    public class VisionContext
    {
        private static VisionContext? _uniqueInstance;
        private static readonly object Locker = new();

        private VisionContext()
        {
        }

        public static VisionContext Instance()
        {
            if (_uniqueInstance == null)
            {
                lock (Locker)
                {
                    _uniqueInstance ??= new VisionContext();
                }
            }

            return _uniqueInstance;
        }

        public ILogger? Log { get; set; }


        public bool Drawable { get; set; }


        public DrawContent DrawContent { get; set; } = new();

        /// <summary>
        /// cache for drawContent
        /// </summary>
        public DrawContent DrawContentCache { get; set; } = new();
    }
}