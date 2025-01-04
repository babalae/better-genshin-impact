using System;
using System.Diagnostics;
using BetterGenshinImpact.Model;
using TOS;
using TOS.Error;
using TOS.Model;

namespace BetterGenshinImpact.Helpers.Upload;

public class TosClientHelper : Singleton<TosClientHelper>
{
    public void Test(string localFileName)
    {
        var ak = "";
        var sk = "";
        // endpoint 若没有指定HTTP协议（HTTP/HTTPS），默认使用 HTTPS
        // Bucket 的 Endpoint，以华北2（北京）为例：https://tos-cn-beijing.volces.com
        var endpoint = "https://tos-cn-beijing.volces.com";
        var region = "cn-beijing";
        // 填写 BucketName
        var bucketName = "seed-data-vendor";
        // 将文件上传到 example_dir 目录下的 example.txt 文件
        var objectKey = "test.txt";

        // 创建TOSClient实例
        var client = TosClientBuilder.Builder().SetAk(ak).SetSk(sk).SetEndpoint(endpoint).SetRegion(region).Build();

        try
        {
            // 创建上传本地文件输入
            var putObjectFromFileInput = new PutObjectFromFileInput()
            {
                Bucket = bucketName,
                Key = objectKey,
                FilePath = localFileName
            };

            // 直接使用文件路径上传文件
            var putObjectFromFileOutput = client.PutObjectFromFile(putObjectFromFileInput);
            Debug.WriteLine("Put object succeeded, request id: {0} ", putObjectFromFileOutput.RequestID);
        }
        catch (TosServerException ex)
        {
            Debug.WriteLine("Put object failed, request id {0}", ex.RequestID);
            Debug.WriteLine("Put object failed, status code {0}", ex.StatusCode);
            Debug.WriteLine("Put object failed, response error code {0}", ex.Code);
            Debug.WriteLine("Put object failed, response error message {0}", ex.Message);
        }
        catch (TosClientException ex)
        {
            Debug.WriteLine("Put object failed, error message {0}", ex.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Put object failed, {0}", ex.Message);
        }
    }
}