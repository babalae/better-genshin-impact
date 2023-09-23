using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vision.Recognition.Helper.Simulator
{
    public class Simulator
    {
        public static PostMessageSimulator PostMessage(IntPtr hWnd)
        {
            return new PostMessageSimulator(hWnd);
        }
    }
}