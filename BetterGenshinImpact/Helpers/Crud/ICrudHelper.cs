using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BetterGenshinImpact.Helpers.Crud;

public interface ICrudHelper<T>
{
    T Insert(T entity);

    ObservableCollection<T> MultiQuery();

    T Update(T entity, Dictionary<string, object> condition);

    bool Delete(Dictionary<string, object> condition);
}
