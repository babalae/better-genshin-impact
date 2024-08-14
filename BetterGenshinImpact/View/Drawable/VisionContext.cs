namespace BetterGenshinImpact.View.Drawable
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

        //public ILogger? Log { get; set; }


        public bool Drawable { get; set; }


        public DrawContent DrawContent { get; set; } = new();
    }
}