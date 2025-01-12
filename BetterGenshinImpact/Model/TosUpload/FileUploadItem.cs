namespace BetterGenshinImpact.Model.TosUpload;

public class FileUploadItem
{
    // 文件路径
    public string? FilePath { get; set; }
    
    // 文件名
    public string? ObjectKey { get; set; }
    
    // 上传ID+
    public string? UploadId { get; set; }
    
    // 上传状态
    public string? Status { get; set; }
}

public enum UploadStatus
{
    // 未上传
    NotUpload,
    
    // 上传中
    Uploading,
    
    // 上传成功
    UploadSuccess,
}