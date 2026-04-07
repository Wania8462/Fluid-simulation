using System;
using System.Collections;
using System.Collections.Generic;

// WARNING: Indexer returns ref — do not hold onto a ref across any Add/Remove/Resize call, as resizing invalidates all refs.
// WARNING: Remove(item) and RemoveAt(index) do not preserve order — the last element is swapped into the removed slot.
public class RefList<T> : IEnumerable<T>
{
    private T[] _items;
    private int _size;

    public int Count => _size;
    public int Capacity => _items.Length;

    private const int defaultSize = 4;

    public RefList(int size = 0)
    {
        if (size == 0)
            _items = new T[defaultSize];

        else
            _items = new T[size];

        _size = size;
    }

    public ref T this[int index]
    {
        get
        {
            if (index >= _size)
                throw new IndexOutOfRangeException();

            return ref _items[index];
        }
    }

    public void Add()
    {
        if (_size == _items.Length)
            Resize(_items.Length * 2);

        _size++;
    }

    public void Add(T item)
    {
        if (_size == _items.Length)
            Resize(_items.Length * 2);

        _items[_size++] = item;
    }

    public void AddRange(IEnumerable<T> collection)
    {
        if (collection is ICollection<T> c)
        {
            EnsureCapacity(_size + c.Count);
            c.CopyTo(_items, _size);
            _size += c.Count;
            return;
        }

        foreach (T item in collection)
            Add(item);
    }

    public void AddRange(T[] array)
    {
        int count = array.Length;
        EnsureCapacity(_size + count);
        Array.Copy(array, 0, _items, _size, count);
        _size += count;
    }

    public ref T Last()
    {
        if (_size == 0)
            throw new InvalidOperationException();

        return ref _items[_size - 1];
    }

    public bool Remove(T item)
    {
        for (int i = 0; i < _size; i++)
        {
            if (EqualityComparer<T>.Default.Equals(_items[i], item))
            {
                _size--;
                _items[i] = _items[_size];
                _items[_size] = default!;
                return true;
            }
        }
        return false;
    }

    public void RemoveLast()
    {
        if (_size == 0)
            throw new InvalidOperationException();

        _size--;
        _items[_size] = default!;
    }

    public void RemoveAt(int index)
    {
        if (index >= _size)
            throw new IndexOutOfRangeException();

        _size--;
        _items[index] = _items[_size];
        _items[_size] = default!;
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _size);
        _size = 0;
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity > _items.Length)
            Resize(capacity);
    }

    // foreach uses duck typing: picks this struct overload, zero allocation
    public RefListEnumerator<T> GetEnumerator() => new(_items, _size);

    // Explicit interface implementations for IEnumerable<T> / LINQ / etc.
    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        for (int i = 0; i < _size; i++)
            yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    private void Resize(int newCapacity)
    {
        T[] newArr = new T[newCapacity];
        Array.Copy(_items, newArr, _size);
        _items = newArr;
    }
}

public struct RefListEnumerator<T>
{
    private readonly T[] _items;
    private readonly int _size;
    private int _index;

    internal RefListEnumerator(T[] items, int size)
    {
        _items = items;
        _size = size;
        _index = -1;
    }

    public bool MoveNext()
    {
        _index++;
        return _index < _size;
    }

    public readonly T Current => _items[_index];
}