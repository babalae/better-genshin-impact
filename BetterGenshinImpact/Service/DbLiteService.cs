using System;
using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using LiteDB;

namespace BetterGenshinImpact.Service;

public class DbLiteService : Singleton<DbLiteService>
{
    public LiteDatabase UserDb { get; set; } = new(Global.Absolute(@"User\User.db"));
    
    
    public void Upsert<T>(string collectionName,string uniqueProperty, T entity) where T : new()
    {
        var collection = UserDb.GetCollection<T>(collectionName);
        var idProperty = typeof(T).GetProperty(uniqueProperty);
        if (idProperty == null)
        {
            throw new ArgumentException("Entity must have an Id property");
        }

        var idValue = idProperty.GetValue(entity);
        if (idValue == null)
        {
            throw new ArgumentException("Id property cannot be null");
        }

        var existingEntity = collection.FindOne(Query.EQ(uniqueProperty, new BsonValue(idValue)));
        if (existingEntity != null)
        {
            collection.Update(entity);
            Debug.WriteLine($"更新数据");
        }
        else
        {
            collection.Insert(entity);
            Debug.WriteLine($"写入数据");
        }
    }
}