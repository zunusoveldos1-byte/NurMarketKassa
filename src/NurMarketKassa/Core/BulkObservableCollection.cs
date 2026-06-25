using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace NurMarketKassa.Core
{
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public BulkObservableCollection() { }

        public BulkObservableCollection(IEnumerable<T> collection) : base(collection) { }

        /// <summary>Заменяет все элементы, вызывая одиночный Reset.</summary>
        public void Reset(IEnumerable<T> newItems)
        {
            Items.Clear();
            foreach (var item in newItems)
                Items.Add(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

    }
}