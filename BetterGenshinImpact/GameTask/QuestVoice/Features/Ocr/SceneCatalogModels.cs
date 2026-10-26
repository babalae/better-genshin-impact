// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>LoadedSceneCatalog：原神自动配音的业务类型。</summary>
public sealed record LoadedSceneCatalog(string AlgorithmVersion, string Sha256, SceneCatalogDefinition Definition);
/// <summary>SceneCatalogDefinition：原神自动配音的业务类型。</summary>
public sealed record SceneCatalogDefinition(string AlgorithmVersion, int Width, int Height,
    SceneTransform Transform, SceneColors Colors, PixelRect NameArea, PixelRect TitleArea, PixelRect TextArea,
    SceneGeometry Geometry, SceneSeparator[] Separators, SceneLayout[] Layouts);
/// <summary>SceneTransform：原神自动配音的业务类型。</summary>
public sealed record SceneTransform(double MinScale, double MaxScale, double AspectTolerance, long MaximumPixels,
    int BorderDepth, int BorderDarkMaximum, double BorderDarkFraction);
/// <summary>SceneColors：原神自动配音的业务类型。</summary>
public sealed record SceneColors(SceneColorRange NameGold, SceneColorRange SeparatorGold, SceneColorRange White);
/// <summary>SceneColorRange：原神自动配音的业务类型。</summary>
public sealed record SceneColorRange(int MinRed, int MinGreen, int MinBlue, int MaxBlue, int MinRedGreen,
    int MinGreenBlue, int MinRedBlue, int MaxSpread);
/// <summary>SceneGeometry：原神自动配音的业务类型。</summary>
public sealed record SceneGeometry(int ComponentMinimumPixels, int ComponentMaximumWidth, int ComponentMinimumHeight,
    int ComponentMaximumHeight, int NameMinimumPixels, int NameMinimumTallComponents, int NameTallHeight,
    int TitleMinimumPixels, int TitleMinimumTallComponents, int TitleTallHeight, int BodyMinimumPixels,
    int BodyMinimumComponents, int BodyTallHeight, int BodyMinimumTallComponents, int PunctuationMinimumComponents,
    int PunctuationMinimumPixels, int PunctuationBaselineDepth, int RubyMinimumPixels, int RubyMinimumComponents,
    int RubyMinimumHeight, int RubyMaximumHeight, int ExtraPixelAllowance, double ExtraPixelFraction,
    int ExtraMinimumAlignedComponents, int BodyCenterTolerance, int BandTolerance);
/// <summary>SceneSeparator：原神自动配音的业务类型。</summary>
public sealed record SceneSeparator(bool HasTitle, PixelRect Bounds, int CenterGap, int MinimumSideRun,
    int MinimumRows, int MaximumThickness, int ContrastOffset, int MinimumContrast);
/// <summary>SceneLayout：原神自动配音的业务类型。</summary>
public sealed record SceneLayout(string Id, bool HasTitle, PixelRect Speaker, SceneInkBand[] Bands, PixelRect[] Dialogue);
/// <summary>SceneInkBand：原神自动配音的业务类型。</summary>
public sealed record SceneInkBand(int Top, int Bottom, string Kind);
