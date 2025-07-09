using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Core.Script.Utils;

public class JsonMerger
{
    private class CacheItem
    {
        public DateTime LastWriteTimeUtc { get; set; }
        public JObject? JObject { get; set; }
    }

    // 用于存储多个文件的缓存
    private static readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem>();

    /// <summary>
    /// 从缓存读取 JObject，如果文件有更新则重新读取解析
    /// </summary>
    /// <param name="filePath">文件完整路径</param>
    /// <returns>JObject</returns>
    public static JObject? GetJObject(string filePath)
    {
        if (!File.Exists(filePath)) return null;
            

        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);

        if (_cache.TryGetValue(filePath, out var cacheItem))
        {
            if (cacheItem.LastWriteTimeUtc == lastWriteTimeUtc)
            {
                // 文件未变更，直接返回缓存的 JObject
                return cacheItem.JObject;
            }
        }

        // 文件有变更或第一次读取
        string jsonText = File.ReadAllText(filePath);
        JObject jObject = JObject.Parse(jsonText);

        _cache[filePath] = new CacheItem
        {
            LastWriteTimeUtc = lastWriteTimeUtc,
            JObject = jObject
        };

        return jObject;
    }

     public static JObject? GetPathingCtrJObject(string dirPath)
     {
         return GetJObject(Path.Combine(dirPath,"control.json5"));
     }
     public static string getMergePathingJson(string? pathingPath)
     {
         if (pathingPath == null)
         {
             return "";
         }
         var controlObj = GetPathingCtrJObject(Path.GetDirectoryName(pathingPath));
         var json = File.ReadAllText(pathingPath);
         if (controlObj == null)
         {
             return json;
         }
         return MergeJson(controlObj,JObject.Parse(json),Path.GetFileNameWithoutExtension(pathingPath));
     }
    public static string MergeJson( string controlJson,string originalJson, string name)
    {

        return MergeJson(JObject.Parse(controlJson),JObject.Parse(originalJson),name);
    }

    public static string MergeJson( JObject control,JObject original, string name)
    {
        // 全局属性覆盖
        if (control["global_cover"] is JObject globalCover)
        {
            ApplyPropertyCover(original, globalCover);
        }

        // 全局对象覆盖
        if (control["obj_global_cover"] is JObject objGlobalCover)
        {
            ApplyObjectCover(original, objGlobalCover);
        }

        // 处理json_list中的特定规则
        if (control["json_list"] is JArray jsonList)
        {
            foreach (var item in jsonList)
            {
                if (item["name"]?.ToString() == name)
                {
                    // 属性覆盖
                    if (item["cover"] is JObject cover)
                    {
                        ApplyPropertyCover(original, cover);
                    }

                    // 对象覆盖
                    if (item["obj_ cover"] is JObject objCover) // 注意空格匹配原始键名
                    {
                        ApplyObjectCover(original, objCover);
                    }

                    break; // 找到匹配项后退出循环
                }
            }
        }

        return original.ToString();
    }

    // 属性覆盖（递归合并）
    private static void ApplyPropertyCover(JObject target, JObject source)
    {
        foreach (var prop in source.Properties())
        {
            var propName = prop.Name;
            var sourceValue = prop.Value;

            // 目标不存在该属性，直接添加
            if (!target.ContainsKey(propName))
            {
                target.Add(propName, sourceValue.DeepClone());
                continue;
            }

            var targetValue = target[propName];

            // 递归处理对象类型
            if (sourceValue is JObject sourceObj && targetValue is JObject targetObj)
            {
                ApplyPropertyCover(targetObj, sourceObj);
            }
            else // 基础类型或数组直接替换
            {
                target[propName] = sourceValue.DeepClone();
            }
        }
    }

    // 对象覆盖（整体替换）
    private static void ApplyObjectCover(JObject target, JObject source)
    {
        foreach (var prop in source.Properties())
        {
            var propName = prop.Name;
            target[propName] = prop.Value.DeepClone();
        }
    }
}