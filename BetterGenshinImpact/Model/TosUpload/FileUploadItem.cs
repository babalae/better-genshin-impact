namespace BetterGenshinImpact.Model.TosUpload;

public class FileUploadItem
{
    // 与 ObjectKey 相同
    public string? Id { get; set; }
    // 文件路径
    public string? FilePath { get; set; }
    
    // 上传 ObjectKey
    public string? ObjectKey { get; set; }
    
    // 上传ID
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