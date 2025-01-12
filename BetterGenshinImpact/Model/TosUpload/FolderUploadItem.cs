namespace BetterGenshinImpact.Model.TosUpload;

public class FolderUploadItem
{
    // 文件夹路径作为Id
    public string? Id { get; set; }
    
    // 文件夹名称
    public string? FolderName { get; set; }
    
    // 上传状态
    public string? Status { get; set; }
}