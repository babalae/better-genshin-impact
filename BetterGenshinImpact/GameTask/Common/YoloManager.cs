using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using Compunet.YoloV8;
using System;

namespace BetterGenshinImpact.GameTask.Common;

// [Obsolete]
// public class YoloManager : Singleton<YoloManager>, IDisposable
// {
//     /// <summary>
//     /// 角色侧面头像分类器
//     /// </summary>
//     public readonly Lazy<YoloV8> AvatarSideIconClassifierLazy = new(() => new YoloV8(Global.Absolute("Assets\\Model\\Common\\avatar_side_classify_sim.onnx")));
//
//     public YoloV8 AvatarSideIconClassifier => AvatarSideIconClassifierLazy.Value;
//
//     public void Dispose()
//     {
//         if (AvatarSideIconClassifierLazy.IsValueCreated)
//         {
//             AvatarSideIconClassifierLazy.Value.Dispose();
//         }
//     }
// }
