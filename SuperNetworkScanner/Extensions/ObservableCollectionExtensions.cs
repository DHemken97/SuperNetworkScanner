using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperNetworkScanner.Extensions
{
    public static class ObservableCollectionExtensions
    {
        public static ObservableCollection<T> AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> add)
        {
            foreach (var item in add)
                collection.Add(item);
            return collection;
        }
    }
}
