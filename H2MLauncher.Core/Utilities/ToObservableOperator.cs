using System.Reactive.Disposables;

namespace H2MLauncher.Core.Utilities;

public static partial class AsyncEnumerableEx
{
    /// <summary>
    /// Converts an async-enumerable sequence to an observable sequence.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">Enumerable sequence to convert to an observable sequence.</param>
    /// <returns>The observable sequence whose elements are pulled from the given enumerable sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public static IObservable<TSource> ToObservable<TSource>(this IAsyncEnumerable<TSource> source)
    {
        return new ToObservableObservable<TSource>(source);
    }

    private sealed class ToObservableObservable<T> : IObservable<T>
    {
        private readonly IAsyncEnumerable<T> _source;

        public ToObservableObservable(IAsyncEnumerable<T> source)
        {
            _source = source;
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            var cts = new CancellationTokenSource();

            async void Core()
            {
                await using var e = _source.GetAsyncEnumerator(cts.Token);
                do
                {
                    bool hasNext;
                    var value = default(T)!;

                    try
                    {
                        hasNext = await e.MoveNextAsync().ConfigureAwait(false);
                        if (hasNext)
                        {
                            value = e.Current;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!cts.Token.IsCancellationRequested)
                        {
                            observer.OnError(ex);
                        }

                        return;
                    }

                    if (!hasNext)
                    {
                        observer.OnCompleted();
                        return;
                    }

                    observer.OnNext(value);
                }
                while (!cts.Token.IsCancellationRequested);
            }

            // Fire and forget
            Core();

            return Disposable.Create(() =>
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            });
        }
    }
}