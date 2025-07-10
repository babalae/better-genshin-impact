using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // 1. 应用全局覆盖规则
        JToken globalCover = control["global_cover"];
        if (globalCover != null && globalCover.Type == JTokenType.Object)
        {
            MergeObject((JObject)globalCover, original, new List<string>());
        }

        // 2. 查找匹配名称的覆盖规则
        JArray jsonList = control["json_list"] as JArray;
        if (jsonList != null)
        {
            foreach (JToken item in jsonList)
            {
                if (item["name"]?.ToString() == name)
                {
                    JToken cover = item["cover"];
                    if (cover != null && cover.Type == JTokenType.Object)
                    {
                        MergeObject((JObject)cover, original, new List<string>());
                    }
                    break;
                }
            }
        }

        return original.ToString();
    }
    private static void MergeObject(JObject control, JObject target, List<string> processedKeys)
    {
        HashSet<string> skipKeys = new HashSet<string>();

        // 处理特殊控制指令
        ProcessSpecialInstructions(control, target, skipKeys);

        // 处理普通属性
        foreach (var prop in control.Properties().ToList())
        {
            string key = prop.Name;
            
            // 跳过已处理键和控制指令
            if (skipKeys.Contains(key)) continue;
            
            JToken controlValue = prop.Value;
            JToken targetValue = target[key];

            // 处理对象类型
            if (controlValue.Type == JTokenType.Object)
            {
                
                if (targetValue == null || targetValue.Type != JTokenType.Object)
                {
                    target[key] = controlValue.DeepClone();
                }
                else
                {
                    MergeObject((JObject)controlValue, (JObject)targetValue, new List<string>());
                }
                continue;
            }

            // 处理数组类型
            if (controlValue.Type == JTokenType.Array)
            {
                target[key] = controlValue.DeepClone();
                continue;
            }

            // 处理其他类型
            target[key] = controlValue.DeepClone();
        }
    }
    private static void ProcessSpecialInstructions(JObject control, JObject target, HashSet<string> skipKeys)
    {
        // 处理_obj_cover指令（对象覆盖）
        JToken objOver = control["_obj_cover"];
        if (objOver != null && objOver.Type == JTokenType.Array)
        {
            foreach (JToken item in (JArray)objOver)
            {
                string propName = item.ToString();
                if (control[propName] != null)
                {
                    target[propName] = control[propName].DeepClone();
                    skipKeys.Add(propName);
                }
            }
            skipKeys.Add("_obj_cover");
        }

        // 处理_arr_add指令（数组合并）
        JToken arrAdd = control["_arr_add"];
        if (arrAdd != null && arrAdd.Type == JTokenType.Array)
        {
            foreach (JToken item in (JArray)arrAdd)
            {
                string propName = item.ToString();
                JToken controlArray = control[propName];
                
                if (controlArray != null && controlArray.Type == JTokenType.Array)
                {
                    JToken targetArray = target[propName];
                    if (targetArray != null && targetArray.Type == JTokenType.Array)
                    {
                        target[propName] = MergeArrays((JArray)controlArray, (JArray)targetArray);
                    }
                    else
                    {
                        target[propName] = controlArray.DeepClone();
                    }
                    skipKeys.Add(propName);
                }
            }
            skipKeys.Add("_arr_add");
        }
    }

    private static JArray MergeArrays(JArray source, JArray target)
    {
        // 合并数组并去重
        List<JToken> result = new List<JToken>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 添加目标数组元素
        foreach (var item in target)
        {
            string json = item.ToString(Newtonsoft.Json.Formatting.None);
            if (seen.Add(json))
            {
                result.Add(item);
            }
        }

        // 添加源数组元素
        foreach (var item in source)
        {
            string json = item.ToString(Newtonsoft.Json.Formatting.None);
            if (seen.Add(json))
            {
                result.Add(item);
            }
        }

        return new JArray(result);
    }
    /*
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
    }*/
}