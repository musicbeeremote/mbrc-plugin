using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Threading;

namespace mbrcPartyMode.Helper
{

    /// <summary>
    /// http://geekswithblogs.net/NewThingsILearned/archive/2008/01/16/have-worker-thread-update-observablecollection-that-is-bound-to-a.aspx
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ObservableCollectionEx<T> : ObservableCollection<T>
    {
        public ObservableCollectionEx()
        {

        }

        private bool _suppressNotification = false;

        // Override the event so this class can access it
        public override event System.Collections.Specialized.NotifyCollectionChangedEventHandler CollectionChanged;

        protected override void OnCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_suppressNotification) return;
            // Be nice - use BlockReentrancy like MSDN said
            using (BlockReentrancy())
            {
                System.Collections.Specialized.NotifyCollectionChangedEventHandler eventHandler = CollectionChanged;
                if (eventHandler == null) return;

                Delegate[] delegates = eventHandler.GetInvocationList();

                foreach (System.Collections.Specialized.NotifyCollectionChangedEventHandler handler in delegates)
                {
                    DispatcherObject dispatcherObject = handler.Target as DispatcherObject;
                    // If the subscriber is a DispatcherObject and different thread
                    if (dispatcherObject != null && dispatcherObject.CheckAccess() == false)
                    {
                        // Invoke handler in the target dispatcher's thread
                        dispatcherObject.Dispatcher.Invoke(DispatcherPriority.DataBind, handler, this, e);
                    }
                    else // Execute handler as is
                    {
                        handler(this, e);
                    }
                }
            }
        }

        public void AddRange(IEnumerable<T> list)
        {
            if (list == null) throw new ArgumentNullException("list");

            _suppressNotification = true;

            foreach (T item in list)
            {
                Add(item);
            }
            _suppressNotification = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void AddEx(T item)
        {
        //Dispatcher    _dispatcher = Dispatcher.CurrentDispatcher;

           
        //     _dispatcher.InvokeIfRequired(() =>
        //   {
        //       if (index > this.Count)
        //           return;
   
        //       _lock.EnterWriteLock();
        //       try
        //       {
        //           base.InsertItem(index, item);
        //       }
        //       finally
        //       {
        //           _lock.ExitWriteLock();
        //       }
        //   }, DispatcherPriority.DataBind);


            _suppressNotification = true;
            Add(item);
            _suppressNotification = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

    }

}