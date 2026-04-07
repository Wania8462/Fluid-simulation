/// <summary>
/// A fixed-size integer array used as a sparse set mapping.
/// Logs an error via Debug.LogError when a read returns -1 (particle not found / slot empty).
/// </summary>
public class SparseArray
{
    private int[] _items;

    public int Length => _items.Length;

    public SparseArray(int length)
    {
        _items = new int[length];
    }

    public int this[int index]
    {
        get
        {
            int v = _items[index];
            if (v == -1)
                throw new System.InvalidOperationException($"There is not particle with ID {index}");
            return v;
        }
        set => _items[index] = value;
    }

    public void Fill(int value) => System.Array.Fill(_items, value);

    public void CopyTo(SparseArray dest, int count) => System.Array.Copy(_items, dest._items, count);
}
