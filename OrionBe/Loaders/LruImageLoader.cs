using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace OrionBe.Loaders;

public class LruImageLoader : BaseWebImageLoader
{
    private static readonly LruCache<string, Bitmap> _cache = new(30);

    public override async Task<Bitmap?> ProvideImageAsync(string url)
    {
        return await _cache.GetOrAddAsync(url, () => LoadAsync(url, null));
    }

    public override async Task<Bitmap?> ProvideImageAsync(string url, IStorageProvider? storageProvider = null)
    {
        return await _cache.GetOrAddAsync(url, () => LoadAsync(url, storageProvider));
    }
}

public class LruCache<TKey, TValue> where TKey : notnull where TValue : class, IDisposable
{
    private readonly int _capacity;
    private readonly LinkedList<(TKey key, TValue value)> _list = new();
    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map = new();

    public LruCache(int capacity)
    {
        _capacity = capacity;
    }

    public async Task<TValue?> GetOrAddAsync(TKey key, Func<Task<TValue?>> factory)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                return node.Value.value;
            }
        }

        var value = await factory();
        if (value == null) return null;

        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                _list.AddFirst(existing);

                value.Dispose();
                return existing.Value.value;
            }

            if (_map.Count >= _capacity)
            {
                var last = _list.Last!;
                _list.RemoveLast();
                _map.Remove(last.Value.key);

                last.Value.value.Dispose();
            }

            var node = new LinkedListNode<(TKey, TValue)>((key, value));
            _list.AddFirst(node);
            _map[key] = node;

            return value;
        }
    }
}