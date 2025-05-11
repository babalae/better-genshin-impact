using System;
using BetterGenshinImpact.Helpers.Extensions;

namespace BetterGenshinImpact.Model;

public class EnumItem<T> where T : Enum
{
    public T Value { get; set; }
    public string DisplayName { get; set; }
    public int Order { get; set; }
    public string EnumName => Value.ToString();

    // 提供一个静态工厂方法来创建实例
    public static EnumItem<T> Create(T value)
    {
        return new EnumItem<T>
        {
            Value = value,
            DisplayName = value.GetDescription(), // 使用扩展方法获取Description
            Order = value.GetOrder()
        };
    }


}
