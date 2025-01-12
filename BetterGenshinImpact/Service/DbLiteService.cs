using System;
using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using LiteDB;

namespace BetterGenshinImpact.Service;

public class DbLiteService : Singleton<DbLiteService>
{
    public LiteDatabase UserDb { get; set; } = new(Global.Absolute(@"User\User.db"));
    
    
    public void Upsert<T>(string collectionName, T entity) where T : new()
    {
        try
        {
            var collection = UserDb.GetCollection<T>(collectionName);
            // 使用 Upsert 方法一次性处理插入或更新
            collection.Upsert(entity);
            Debug.WriteLine($"数据更新/插入成功");
        }
        catch (LiteException ex)
        {
            Debug.WriteLine($"数据操作失败: {ex.Message}");
            throw;
        }
    }
}