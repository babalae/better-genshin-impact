using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoMusicGame.Assets;

public class AutoMusicAssets : BaseAssets<AutoMusicAssets>
{
    public RecognitionObject UiLeftTopAlbumIcon;
    public RecognitionObject BtnPause;
    public RecognitionObject AlbumMusicComplate;
    public RecognitionObject BtnList;

    private AutoMusicAssets()
    {
        UiLeftTopAlbumIcon = new RecognitionObject
        {
            Name = "UiLeftTopAlbumIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"AutoMusicGame", "ui_left_top_album_icon.png"),
            RegionOfInterest = new Rect(0, 0, (int)(150 * AssetScale), (int)(120 * AssetScale)),
        }.InitTemplate();
        BtnPause = new RecognitionObject
        {
            Name = "BtnPause",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"AutoMusicGame", "btn_pause.png"),
            RegionOfInterest = CaptureRect.CutRightTop(0.2, 0.2),
        }.InitTemplate();
        AlbumMusicComplate = new RecognitionObject
        {
            Name = "AlbumMusicComplate",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"AutoMusicGame", "album_music_complate.png"),
            RegionOfInterest = new Rect( (int)(900 * AssetScale),(int)(320 * AssetScale), (int)(100 * AssetScale), (int)(80 * AssetScale)),
        }.InitTemplate();
        BtnList = new RecognitionObject
        {
            Name = "BtnList",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"AutoMusicGame", "btn_list.png"),
            RegionOfInterest = CaptureRect.CutRightBottom(0.4, 0.2),
        }.InitTemplate();
    }
}