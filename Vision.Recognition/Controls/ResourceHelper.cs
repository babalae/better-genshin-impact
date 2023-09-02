using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Vision.Recognition.Controls
{
    public class ResourceHelper
    {
        public static T GetResource<T>(string key)
        {
            return Application.Current.TryFindResource(key) is T resource ? resource : default;
        }
    }
}
