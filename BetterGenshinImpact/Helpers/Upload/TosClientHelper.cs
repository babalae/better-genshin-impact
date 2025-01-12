using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using Newtonsoft.Json;
using TOS;
using TOS.Error;
using TOS.Model;
using JsonSerializer = System.Text.Json.JsonSerializer;
using BetterGenshinImpact.Model.TosUpload;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Helpers.Upload;

public delegate void UploadProgressCallback(long uploadedBytes, long totalBytes, double percentage);

public class TosClientHelper
{
    private readonly string _configPath = Global.Absolute("User/tos.json");
    private TosConfig _config;
    private ITosClient _client;
    private readonly DbLiteService _dbService = DbLiteService.Instance;

    private class TosConfig
    {
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string Endpoint { get; set; } = "https://tos-cn-beijing.volces.com";
        public string Region { get; set; } = "cn-beijing";
        public string BucketName { get; set; } = "seed-data-vendor";
    }

    public TosClientHelper()
    {
        LoadConfig();
        InitClient();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var jsonString = File.ReadAllText(_configPath);
                _config = JsonConvert.DeserializeObject<TosConfig>(jsonString);
                if (_config == null)
                {
                    throw new Exception("Failed to deserialize TOS config");
                }
            }
            else
            {
                throw new FileNotFoundException("TOS config file not found");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load TOS config: {ex.Message}");
            _config = new TosConfig();
        }
    }

    private void InitClient()
    {
        try
        {
            _client = TosClientBuilder.Builder()
                .SetAk(_config.AccessKey)
                .SetSk(_config.SecretKey)
                .SetEndpoint(_config.Endpoint)
                .SetRegion(_config.Region)
                .Build();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize TOS client: {ex.Message}");
        }
    }

    public void UploadFile(string localFileName, string? objectKey = null)
    {
        Debug.WriteLine($"Uploading file: {localFileName}, object key: {objectKey}");
        if (string.IsNullOrEmpty(_config.AccessKey) || string.IsNullOrEmpty(_config.SecretKey))
        {
            Debug.WriteLine("TOS credentials not configured");
            return;
        }

        try
        {
            objectKey ??= Path.GetFileName(localFileName);
            
            // 检查是否已经上传成功
            // var collection = _dbService.UserDb.GetCollection<FileUploadItem>("FileUploads");
            // var existingItem = collection.FindById(objectKey);
            // if (existingItem?.Status == UploadStatus.UploadSuccess.ToString())
            // {
            //     Debug.WriteLine($"File {objectKey} already uploaded successfully");
            //     return;
            // }
            
            var fileUploadItem = new FileUploadItem
            {
                Id = objectKey,
                FilePath = localFileName,
                ObjectKey = objectKey,
                Status = UploadStatus.Uploading.ToString()
            };
            
            _dbService.Upsert("FileUploads", fileUploadItem);


            var putObjectFromFileInput = new PutObjectFromFileInput
            {
                Bucket = _config.BucketName,
                Key = objectKey,
                FilePath = localFileName
            };

            var putObjectFromFileOutput = _client.PutObjectFromFile(putObjectFromFileInput);
            Debug.WriteLine($"Put object succeeded, request id: {putObjectFromFileOutput.RequestID}");
            
            fileUploadItem.Status = UploadStatus.UploadSuccess.ToString();
            _dbService.Upsert("FileUploads", fileUploadItem);
            
        }
        catch (TosServerException ex)
        {
            Debug.WriteLine($"Put object failed, request id {ex.RequestID}");
            Debug.WriteLine($"Put object failed, status code {ex.StatusCode}");
            Debug.WriteLine($"Put object failed, response error code {ex.Code}");
            Debug.WriteLine($"Put object failed, response error message {ex.Message}");
        }
        catch (TosClientException ex)
        {
            Debug.WriteLine($"Put object failed, error message {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Put object failed, {ex.Message}");
        }
    }

    /// <summary>
    /// 分片上传文件
    /// </summary>
    /// <param name="localFileName">本地文件路径</param>
    /// <param name="objectKey">对象存储路径，如果为空则使用文件名</param>
    /// <param name="partSize">分片大小（字节），默认20MB</param>
    public void UploadLargeFile(string localFileName, string? objectKey = null, long partSize = 20 * 1024 * 1024, UploadProgressCallback? progressCallback = null)
    {
        Debug.WriteLine($"Uploading file: {localFileName}, object key: {objectKey}");
        if (string.IsNullOrEmpty(_config.AccessKey) || string.IsNullOrEmpty(_config.SecretKey))
        {
            Debug.WriteLine("TOS credentials not configured");
            return;
        }

        objectKey ??= Path.GetFileName(localFileName);
        
        // 检查是否已经上传成功
        // var collection = _dbService.UserDb.GetCollection<FileUploadItem>("FileUploads");
        // var existingItem = collection.FindById(objectKey);
        // if (existingItem?.Status == UploadStatus.UploadSuccess.ToString())
        // {
        //     Debug.WriteLine($"File {objectKey} already uploaded successfully");
        //     progressCallback?.Invoke(100, 100, 100);
        //     return;
        // }

        string uploadID = null;

        var fileUploadItem = new FileUploadItem
        {
            Id = objectKey,
            FilePath = localFileName,
            ObjectKey = objectKey,
            Status = UploadStatus.Uploading.ToString()
        };
        
        _dbService.Upsert("FileUploads", fileUploadItem);
        
        try
        {
            // 1. 初始化分片上传
            var createMultipartUploadInput = new CreateMultipartUploadInput
            {
                Bucket = _config.BucketName,
                Key = objectKey,
                ACL = ACLType.ACLPrivate,
                StorageClass = StorageClassType.StorageClassIa
            };
            var createMultipartUploadOutput = _client.CreateMultipartUpload(createMultipartUploadInput);
            uploadID = createMultipartUploadOutput.UploadID;
            Debug.WriteLine($"CreateMultipartUpload succeeded, upload id: {uploadID}");

            fileUploadItem.UploadId = uploadID;
            _dbService.Upsert("FileUploads", fileUploadItem);

            // 2. 计算分片信息
            var fileInfo = new FileInfo(localFileName);
            var fileSize = fileInfo.Length;
            var partCount = (int)Math.Ceiling((double)fileSize / partSize);
            var parts = new UploadedPart[partCount];
            long totalUploadedBytes = 0;

            // 3. 分片上传
            using (var fileStream = File.Open(localFileName, FileMode.Open, FileAccess.Read))
            {
                for (var i = 0; i < partCount; i++)
                {
                    var offset = partSize * i;
                    fileStream.Seek(offset, SeekOrigin.Begin);
                    var currentPartSize = Math.Min(partSize, fileSize - offset);

                    var uploadPartInput = new UploadPartInput
                    {
                        Bucket = _config.BucketName,
                        Key = objectKey,
                        UploadID = uploadID,
                        PartNumber = i + 1,
                        Content = fileStream,
                        ContentLength = currentPartSize
                    };

                    var uploadPartOutput = _client.UploadPart(uploadPartInput);
                    parts[i] = new UploadedPart { PartNumber = i + 1, ETag = uploadPartOutput.ETag };
                    
                    // 更新进度
                    totalUploadedBytes += currentPartSize;
                    var percentage = (double)totalUploadedBytes / fileSize * 100;
                    progressCallback?.Invoke(totalUploadedBytes, fileSize, percentage);
                    
                    Debug.WriteLine($"UploadPart {i + 1}/{partCount} succeeded");
                }
            }

            // 4. 完成分片上传
            var completeMultipartUploadInput = new CompleteMultipartUploadInput
            {
                Bucket = _config.BucketName,
                Key = objectKey,
                UploadID = uploadID,
                Parts = parts
            };
            var completeMultipartUploadOutput = _client.CompleteMultipartUpload(completeMultipartUploadInput);
            Debug.WriteLine($"CompleteMultipartUpload succeeded, request id: {completeMultipartUploadOutput.RequestID}");

            fileUploadItem.Status = UploadStatus.UploadSuccess.ToString();
            _dbService.Upsert("FileUploads", fileUploadItem);
        }
        catch (TosServerException ex)
        {
            Debug.WriteLine($"Multipart upload failed, request id: {ex.RequestID}");
            Debug.WriteLine($"Status code: {ex.StatusCode}, Error code: {ex.Code}");
            Debug.WriteLine($"Error message: {ex.Message}");
        }
        catch (TosClientException ex)
        {
            Debug.WriteLine($"Multipart upload failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Multipart upload failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 列举指定上传ID的已上传分片信息
    /// </summary>
    /// <param name="objectKey">对象键</param>
    /// <param name="uploadID">分片上传ID</param>
    /// <returns>已上传的分片列表</returns>
    public List<UploadedPart> ListUploadedParts(string objectKey, string uploadID)
    {
        if (string.IsNullOrEmpty(_config.AccessKey) || string.IsNullOrEmpty(_config.SecretKey))
        {
            Debug.WriteLine("TOS credentials not configured");
            return null;
        }

        var allParts = new List<UploadedPart>();
        try
        {
            var truncated = true;
            var marker = 0;

            while (truncated)
            {
                var listPartsInput = new ListPartsInput
                {
                    Bucket = _config.BucketName,
                    Key = objectKey,
                    UploadID = uploadID,
                    PartNumberMarker = marker
                };

                var listPartsOutput = _client.ListParts(listPartsInput);
                truncated = listPartsOutput.IsTruncated;
                marker = listPartsOutput.NextPartNumberMarker;

                Debug.WriteLine($"ListParts succeeded, request id: {listPartsOutput.RequestID}");
                Debug.WriteLine($"ListParts succeeded, upload id: {uploadID}");

                foreach (var part in listPartsOutput.Parts)
                {
                    Debug.WriteLine($"Part {part.PartNumber}: ETag={part.ETag}, Size={part.Size}");
                    allParts.Add(new UploadedPart
                    {
                        PartNumber = part.PartNumber,
                        ETag = part.ETag
                    });
                }
            }

            return allParts;
        }
        catch (TosServerException ex)
        {
            Debug.WriteLine($"ListParts failed, request id: {ex.RequestID}");
            Debug.WriteLine($"Status code: {ex.StatusCode}, Error code: {ex.Code}");
            Debug.WriteLine($"Error message: {ex.Message}");
        }
        catch (TosClientException ex)
        {
            Debug.WriteLine($"ListParts failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ListParts failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 恢复未完成的分片上传
    /// </summary>
    /// <param name="objectKey">对象键</param>
    /// <param name="uploadID">分片上传ID</param>
    /// <param name="localFileName">本地文件路径</param>
    /// <param name="partSize">分片大小</param>
    public void ResumableUpload(string objectKey, string uploadID, string localFileName, long partSize = 20 * 1024 * 1024, UploadProgressCallback? progressCallback = null)
    {
        // 检查是否已经上传成功
        // var collection = _dbService.UserDb.GetCollection<FileUploadItem>("FileUploads");
        // var existingItem = collection.FindById(objectKey);
        // if (existingItem?.Status == UploadStatus.UploadSuccess.ToString())
        // {
        //     Debug.WriteLine($"File {objectKey} already uploaded successfully");
        //     progressCallback?.Invoke(100, 100, 100);
        //     return;
        // }

        var fileUploadItem = new FileUploadItem
        {
            Id = objectKey,
            FilePath = localFileName,
            ObjectKey = objectKey,
            UploadId = uploadID,
            Status = UploadStatus.Uploading.ToString()
        };
        
        _dbService.Upsert("FileUploads", fileUploadItem);
        
        try
        {
            var existingParts = ListUploadedParts(objectKey, uploadID);
            if (existingParts == null)
            {
                Debug.WriteLine("Failed to get existing parts, starting new upload");
                UploadLargeFile(localFileName, objectKey, partSize, progressCallback);
                return;
            }

            var fileInfo = new FileInfo(localFileName);
            var fileSize = fileInfo.Length;
            var partCount = (int)Math.Ceiling((double)fileSize / partSize);
            var parts = new UploadedPart[partCount];
            long totalUploadedBytes = 0;

            // 复制已上传的分片信息并计算已上传的字节数
            foreach (var part in existingParts)
            {
                parts[part.PartNumber - 1] = part;
                totalUploadedBytes += Math.Min(partSize, fileSize - (part.PartNumber - 1) * partSize);
            }

            // 报告初始进度
            var initialPercentage = (int)((double)totalUploadedBytes / fileSize * 100);
            progressCallback?.Invoke(totalUploadedBytes, fileSize, initialPercentage);

            // 上传缺失的分片
            using (var fileStream = File.Open(localFileName, FileMode.Open, FileAccess.Read))
            {
                for (var i = 0; i < partCount; i++)
                {
                    if (parts[i] != null) continue; // 跳过已上传的分片

                    var offset = partSize * i;
                    fileStream.Seek(offset, SeekOrigin.Begin);
                    var currentPartSize = Math.Min(partSize, fileSize - offset);

                    var uploadPartInput = new UploadPartInput
                    {
                        Bucket = _config.BucketName,
                        Key = objectKey,
                        UploadID = uploadID,
                        PartNumber = i + 1,
                        Content = fileStream,
                        ContentLength = currentPartSize
                    };

                    var uploadPartOutput = _client.UploadPart(uploadPartInput);
                    parts[i] = new UploadedPart { PartNumber = i + 1, ETag = uploadPartOutput.ETag };
                    
                    // 更新进度
                    totalUploadedBytes += currentPartSize;
                    var percentage = (int)((double)totalUploadedBytes / fileSize * 100);
                    progressCallback?.Invoke(totalUploadedBytes, fileSize, percentage);
                    
                    Debug.WriteLine($"UploadPart {i + 1}/{partCount} succeeded");
                }
            }

            // 完成分片上传
            var completeMultipartUploadInput = new CompleteMultipartUploadInput
            {
                Bucket = _config.BucketName,
                Key = objectKey,
                UploadID = uploadID,
                Parts = parts
            };
            var completeMultipartUploadOutput = _client.CompleteMultipartUpload(completeMultipartUploadInput);
            Debug.WriteLine($"CompleteMultipartUpload succeeded, request id: {completeMultipartUploadOutput.RequestID}");

            fileUploadItem.Status = UploadStatus.UploadSuccess.ToString();
            _dbService.Upsert("FileUploads", fileUploadItem);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ResumableUpload failed: {ex.Message}");
        }
    }

    public void DeleteObject(string objectKey)
    {
        if (string.IsNullOrEmpty(_config.AccessKey) || string.IsNullOrEmpty(_config.SecretKey))
        {
            Debug.WriteLine("TOS credentials not configured");
            return;
        }

        try
        {
            var deleteObjectInput = new DeleteObjectInput
            {
                Bucket = _config.BucketName,
                Key = objectKey
            };

            var deleteObjectOutput = _client.DeleteObject(deleteObjectInput);
            Debug.WriteLine($"DeleteObject succeeded, request id: {deleteObjectOutput.RequestID}");
        }
        catch (TosServerException ex)
        {
            Debug.WriteLine($"DeleteObject failed, request id: {ex.RequestID}");
            Debug.WriteLine($"Status code: {ex.StatusCode}, Error code: {ex.Code}");
            Debug.WriteLine($"Error message: {ex.Message}");
        }
        catch (TosClientException ex)
        {
            Debug.WriteLine($"DeleteObject failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DeleteObject failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 列举存储桶中的对象
    /// </summary>
    /// <param name="maxKeys">最大返回的对象数量，默认10个</param>
    /// <returns>对象列表，如果发生错误则返回null</returns>
    public ListedObject[] ListObjects(int maxKeys = 10)
    {
        if (string.IsNullOrEmpty(_config.AccessKey) || string.IsNullOrEmpty(_config.SecretKey))
        {
            Debug.WriteLine("TOS credentials not configured");
            return [];
        }

        try
        {
            var listObjectsInput = new ListObjectsInput
            {
                Bucket = _config.BucketName,
                MaxKeys = maxKeys
            };

            var listObjectsOutput = _client.ListObjects(listObjectsInput);
           
            

            return listObjectsOutput.Contents;
        }
        catch (TosServerException ex)
        {
            Debug.WriteLine($"ListObjects failed, request id: {ex.RequestID}");
            Debug.WriteLine($"Status code: {ex.StatusCode}, Error code: {ex.Code}");
            Debug.WriteLine($"Error message: {ex.Message}");
        }
        catch (TosClientException ex)
        {
            Debug.WriteLine($"ListObjects failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ListObjects failed: {ex.Message}");
        }

        return [];
    }
}