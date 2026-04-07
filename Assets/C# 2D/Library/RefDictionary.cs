using System;
using System.Collections;
using System.Collections.Generic;

// Open-addressing hash map with ref indexer.
// WARNING: Do not hold onto a ref across any Add/Remove call — resizing invalidates refs.
public class RefDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
{
    private struct Entry
    {
        public TKey Key;
        public TValue Value;
        public int Hash; // 0 = empty, int.MinValue = deleted (tombstone)
    }

    private const int Empty = 0;
    private const int Tombstone = int.MinValue;
    private const float LoadFactor = 0.72f;

    private Entry[] _entries;
    private int _count;

    public int Count => _count;

    public RefDictionary(int capacity = 16)
    {
        _entries = new Entry[NextPow2(Math.Max(capacity, 4))];
    }

    public ref TValue this[TKey key]
    {
        get
        {
            int index = FindIndex(key);
            if (index < 0)
                throw new KeyNotFoundException($"Key not found: {key}");
            return ref _entries[index].Value;
        }
    }

    public void Add(TKey key, TValue value)
    {
        if (_count >= _entries.Length * LoadFactor)
            Resize();

        int hash = GetHash(key);
        int mask = _entries.Length - 1;
        int index = hash & mask;
        int firstTombstone = -1;

        while (true)
        {
            ref var entry = ref _entries[index];

            if (entry.Hash == Empty)
            {
                int insertAt = firstTombstone >= 0 ? firstTombstone : index;
                ref var slot = ref _entries[insertAt];
                slot.Key = key;
                slot.Value = value;
                slot.Hash = hash;
                _count++;
                return;
            }

            if (entry.Hash == Tombstone)
            {
                if (firstTombstone < 0) firstTombstone = index;
            }
            else if (entry.Hash == hash && EqualityComparer<TKey>.Default.Equals(entry.Key, key))
            {
                throw new ArgumentException($"Key already exists: {key}");
            }

            index = (index + 1) & mask;
        }
    }

    public void Remove(TKey key)
    {
        int index = FindIndex(key);
        if (index < 0)
            throw new KeyNotFoundException();

        ref var entry = ref _entries[index];
        entry.Hash = Tombstone;
        entry.Key = default!;
        entry.Value = default!;
        _count--;
    }

    public bool TryRemove(TKey key)
    {
        int index = FindIndex(key);
        if (index < 0) return false;

        ref var entry = ref _entries[index];
        entry.Hash = Tombstone;
        entry.Key = default!;
        entry.Value = default!;
        _count--;
        return true;
    }

    public bool ContainsKey(TKey key) => FindIndex(key) >= 0;

    public bool TryGetValue(TKey key, out TValue value)
    {
        int index = FindIndex(key);
        if (index < 0)
        {
            value = default!;
            return false;
        }
        value = _entries[index].Value;
        return true;
    }

    // Returns ref to value, adding a default entry if key is missing.
    // Useful for in-place modification without a separate Add call.
    public ref TValue GetOrAdd(TKey key)
    {
        if (_count >= _entries.Length * LoadFactor)
            Resize();

        int hash = GetHash(key);
        int mask = _entries.Length - 1;
        int index = hash & mask;
        int firstTombstone = -1;

        while (true)
        {
            ref var entry = ref _entries[index];

            if (entry.Hash == Empty)
            {
                int insertAt = firstTombstone >= 0 ? firstTombstone : index;
                ref var slot = ref _entries[insertAt];
                slot.Key = key;
                slot.Value = default!;
                slot.Hash = hash;
                _count++;
                return ref slot.Value;
            }

            if (entry.Hash == Tombstone)
            {
                if (firstTombstone < 0) firstTombstone = index;
            }
            else if (entry.Hash == hash && EqualityComparer<TKey>.Default.Equals(entry.Key, key))
            {
                return ref entry.Value;
            }

            index = (index + 1) & mask;
        }
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var entry in _entries)
            if (entry.Hash != Empty && entry.Hash != Tombstone)
                yield return new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int FindIndex(TKey key)
    {
        int hash = GetHash(key);
        int mask = _entries.Length - 1;
        int index = hash & mask;

        while (true)
        {
            ref var entry = ref _entries[index];
            if (entry.Hash == Empty) return -1;
            if (entry.Hash == hash && entry.Hash != Tombstone &&
                EqualityComparer<TKey>.Default.Equals(entry.Key, key))
                return index;
            index = (index + 1) & mask;
        }
    }

    private int GetHash(TKey key)
    {
        int h = EqualityComparer<TKey>.Default.GetHashCode(key!);
        // Perturb to avoid clustering and ensure h is never a sentinel value
        h ^= h >> 16;
        if (h == Empty) h = 1;
        if (h == Tombstone) h = Tombstone + 1;
        return h;
    }

    private void Resize()
    {
        var old = _entries;
        _entries = new Entry[old.Length * 2];
        _count = 0;
        foreach (var entry in old)
            if (entry.Hash != Empty && entry.Hash != Tombstone)
                Add(entry.Key, entry.Value);
    }

    private static int NextPow2(int n)
    {
        n--;
        n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16;
        return n + 1;
    }
}