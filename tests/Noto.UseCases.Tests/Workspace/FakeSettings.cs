using Noto.Core.Settings;
using Noto.Core.Storage;

namespace Noto.UseCases.Tests.Workspace;

/// <summary>
/// The store's contract in memory: present means right type and valid.
/// </summary>
internal sealed class FakeSettings : ISettingsStore
{
    private readonly Dictionary<string, object> _rows = new(StringComparer.Ordinal);

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public List<string> Writes { get; } = [];

    public string? FailWritesTo { get; init; }

    public void Seed(string name, object value) => _rows[name] = value;

    public object? Raw(string name) => _rows.GetValueOrDefault(name);

    public T Read<T>(SettingKey<T> key) => TryRead(key, out T value) ? value : key.Default;

    public bool TryRead<T>(SettingKey<T> key, out T value)
    {
        if (_rows.TryGetValue(key.Name, out object? raw) && raw is T typed && key.IsValid(typed))
        {
            value = typed;
            return true;
        }

        value = key.Default;
        return false;
    }

    public bool Write<T>(SettingKey<T> key, T value)
    {
        if (!key.IsValid(value))
        {
            return false;
        }

        if (key.Name == FailWritesTo)
        {
            throw new StorageException(StorageFailure.WriteFailed, "Simulated failure.");
        }

        _rows[key.Name] = value!;
        Writes.Add(key.Name);
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key));
        return true;
    }
}
