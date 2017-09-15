using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityTracery {
  /// <summary>
  /// Provides some helper functions that keep pools of lists of certain types to
  /// avoid creating new ones during Update functions.
  /// </summary>
  public static class ListPool {
    private static Dictionary<Type, List<IEnumerable>> list_pool = new Dictionary<Type, IEnumerable>();

    /// <summary>
    /// If there are any free lists of the given type, return one.
    /// Otherwise, create a new one.
    /// </summary>
    public static List<T> GetOrCreateList<T>() {
      var type = typeof(T);
      if (list_pool.ContainsKey(type)) {
        if (list_pool[type].Count > 0) {
          var list = list_pool[type][0].Cast<T>().AsList();
          list_pool[type].RemoveAt(0);
          return list;
        }
      }
      return new List<T>();
    }

    public static void FreeList<T>(this List<T> list) {
      list.Clear();
      var type = typeof(T);
      if (list_pool.ContainsKey(type)) {
        list_pool[type].Add(list);
      } else {
        if (list_pool[type].Contains(list)) {
          return;
        }
        list_pool[type].Add(list);
      }
    }
  }
}
