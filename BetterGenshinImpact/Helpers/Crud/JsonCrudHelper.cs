using BetterGenshinImpact.Service;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace BetterGenshinImpact.Helpers.Crud;

public class JsonCrudHelper<T> : ICrudHelper<T> where T : class
{
    private readonly string _filePath;
    private ObservableCollection<T> Items { get; }
    private readonly ReaderWriterLockSlim _lock = new();

    public JsonCrudHelper(string filePath)
    {
        _filePath = filePath;
        Items = LoadFromFile();
        Items.CollectionChanged += ItemsCollectionChanged;
    }

    private void ItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _lock.EnterWriteLock();
        try
        {
            SaveToFile();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public T Insert(T entity)
    {
        _lock.EnterWriteLock();
        Items.Add(entity);
        SaveToFile();
        return entity;
    }

    public ObservableCollection<T> MultiQuery()
    {
        _lock.EnterReadLock();
        try
        {
            return Items;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public T Update(T entity, Dictionary<string, object> condition)
    {
        var index = Items.ToList().FindIndex(e => MatchesCondition(e, condition));
        if (index >= 0)
        {
            Items[index] = entity;
            return entity;
        }
        throw new KeyNotFoundException("Entity not found with the given condition.");
    }

    public bool Delete(Dictionary<string, object> condition)
    {
        var index = Items.ToList().FindIndex(e => MatchesCondition(e, condition));
        if (index >= 0)
        {
            Items.RemoveAt(index);
            return true;
        }
        return false;
    }

    private ObservableCollection<T> LoadFromFile()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<ObservableCollection<T>>(json, ConfigService.JsonOptions) ?? [];
    }

    private void SaveToFile()
    {
        var json = JsonSerializer.Serialize(Items, ConfigService.JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private bool MatchesCondition(T entity, Dictionary<string, object> condition)
    {
        foreach (var kvp in condition)
        {
            var property = typeof(T).GetProperty(kvp.Key);
            if (property == null || !kvp.Value.Equals(property.GetValue(entity)))
            {
                return false;
            }
        }
        return true;
    }
}
