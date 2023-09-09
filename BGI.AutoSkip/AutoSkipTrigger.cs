using System.Drawing;
using Vision.Recognition.Task;

namespace BGI.AutoSkip
{
    /// <summary>
    /// 先不用插件化的模式
    /// </summary>
    public class AutoSkipTrigger 
    {
        public bool IsEnabled { get; private set; }
        public void Init(ITaskContext context)
        {
            throw new NotImplementedException();
        }

        public void OnCapture(Bitmap bitmap)
        {

            throw new NotImplementedException();
        }
    }
}