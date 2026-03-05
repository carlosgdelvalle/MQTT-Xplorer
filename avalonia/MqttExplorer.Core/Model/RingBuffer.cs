namespace MqttExplorer.Core.Model;

public class RingBuffer<T> where T : ILengthAware
{
    private readonly List<T> _items = new();
    private int _start;
    private int _end;
    private int _usage;

    public int Capacity { get; private set; }
    public int MaxItems { get; private set; }

    public RingBuffer(int capacity, int maxItems = int.MaxValue)
    {
        Capacity = capacity;
        MaxItems = maxItems;
    }

    public RingBuffer<T> Clone()
    {
        var clone = new RingBuffer<T>(Capacity, MaxItems);
        foreach (var item in ToArray())
        {
            clone.Add(item);
        }

        return clone;
    }

    public IReadOnlyList<T> ToArray() => _items.Skip(_start).Take(_end - _start).ToArray();

    public int Count => _end - _start;

    public T? Last => _end > _start ? _items[_end - 1] : default;

    public void SetCapacity(int items, int bytes)
    {
        MaxItems = items;
        Capacity = bytes;
    }

    public void Add(T item)
    {
        EnforceCapacity(item.Length);
        _usage += item.Length;
        if (_end == _items.Count)
        {
            _items.Add(item);
        }
        else
        {
            _items[_end] = item;
        }

        _end++;

        if (_end > Math.Max(10, 10 * MaxItems))
        {
            Compact();
        }
    }

    private void EnforceCapacity(int addedSize)
    {
        var remaining = Capacity - (_usage + addedSize);
        if (remaining < 0)
        {
            FreeSomeSpace(-remaining);
        }

        while (_end - _start >= MaxItems)
        {
            DropFirst();
        }
    }

    private void FreeSomeSpace(int requiredSpace)
    {
        while (Capacity - _usage < requiredSpace && _usage > 0)
        {
            DropFirst();
        }
    }

    private void DropFirst()
    {
        if (_start >= _end)
        {
            return;
        }

        var first = _items[_start];
        _usage -= first.Length;
        _start++;
    }

    private void Compact()
    {
        var compact = ToArray().ToList();
        _items.Clear();
        _items.AddRange(compact);
        _start = 0;
        _end = _items.Count;
    }
}
