namespace CheckersApi.Core.Cache;

public interface ILruCache<TKey, TValue> where TKey : notnull
{
    bool TryGet(TKey key, out TValue? value);
    void Set(TKey key, TValue value);
    void Remove(TKey key);
    int Count { get; }
    void Clear();
}

public class MemoryLruCache<TKey, TValue> : ILruCache<TKey, TValue> where TKey : notnull
{
    private class CacheNode
    {
        public TKey Key { get; }
        public TValue Payload { get; set; }
        public DateTime Expiration { get; set; }

        public CacheNode(TKey key, TValue payload, DateTime expiration)
        {
            Key = key;
            Payload = payload;
            Expiration = expiration;
        }
    }

    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<CacheNode>> _map;
    private readonly LinkedList<CacheNode> _list;

    public MemoryLruCache(int capacity = 20000, TimeSpan? ttl = null)
    {
        _capacity = capacity > 0 ? capacity : 20000;
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
        _map = new Dictionary<TKey, LinkedListNode<CacheNode>>(_capacity);
        _list = new LinkedList<CacheNode>();
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (DateTime.UtcNow > node.Value.Expiration)
                {
                    // Expired
                    _list.Remove(node);
                    _map.Remove(key);
                    value = default;
                    return false;
                }

                // Move to MRU position (head of list)
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Payload;
                return true;
            }

            value = default;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            var exp = DateTime.UtcNow.Add(_ttl);

            if (_map.TryGetValue(key, out var existingNode))
            {
                existingNode.Value.Payload = value;
                existingNode.Value.Expiration = exp;
                _list.Remove(existingNode);
                _list.AddFirst(existingNode);
                return;
            }

            // Evict LRU node if at capacity
            if (_map.Count >= _capacity)
            {
                var lruNode = _list.Last;
                if (lruNode != null)
                {
                    _list.RemoveLast();
                    _map.Remove(lruNode.Value.Key);
                }
            }

            var newNode = new CacheNode(key, value, exp);
            var linkedNode = _list.AddFirst(newNode);
            _map[key] = linkedNode;
        }
    }

    public void Remove(TKey key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _map.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
            _map.Clear();
        }
    }
}
