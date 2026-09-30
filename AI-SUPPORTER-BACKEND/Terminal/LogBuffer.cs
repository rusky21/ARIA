using System.Text;

namespace AI_Supporter;

public sealed class LogBuffer : TextWriter
{
    private readonly TextWriter _original;
    private readonly StringBuilder _buffer = new();
    private int _bufferedCount;
    private volatile bool _paused;

    public LogBuffer(TextWriter original)
    {
        _original = original;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (!value) FlushBuffer();
        }
    }

    public void TogglePause()
    {
        _paused = !_paused;
        if (!_paused) FlushBuffer();
    }

    private void FlushBuffer()
    {
        lock (_buffer)
        {
            if (_bufferedCount == 0) return;
            _original.Write(_buffer.ToString());
            _buffer.Clear();
            _bufferedCount = 0;
        }
    }

    public override void Write(char value)
    {
        if (_paused)
        {
            lock (_buffer)
            {
                _buffer.Append(value);
                if (value == '\n') _bufferedCount++;
            }
        }
        else
        {
            _original.Write(value);
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (_paused)
        {
            lock (_buffer)
            {
                _buffer.Append(value);
                _bufferedCount += ValueCountLines(value);
            }
        }
        else
        {
            _original.Write(value);
        }
    }

    public override void WriteLine(string? value)
    {
        if (_paused)
        {
            lock (_buffer)
            {
                _buffer.AppendLine(value);
                _bufferedCount++;
            }
        }
        else
        {
            _original.WriteLine(value);
        }
    }

    private static int ValueCountLines(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
            if (s[i] == '\n') count++;
        return count;
    }
}
