using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The <c>noto://</c> scheme's registration (#192, ADR-022), against a fake registry: per user, opening this copy
/// with the link as its one argument, and removed only when it is this copy's. The real registry is never touched.
/// </summary>
public sealed class ProtocolRegistrationTests
{
    private const string Executable = @"C:\Users\me\AppData\Local\Noto.App\current\Noto.exe";
    private const string Command = @"Software\Classes\noto\shell\open\command";

    [Fact]
    public void Registering_writes_the_per_user_scheme_opening_this_copy_with_the_link()
    {
        var registry = new FakeRegistry();

        Assert.True(new ProtocolRegistration(Executable, registry).Register());

        Assert.Equal("URL:Noto", registry.Values[(@"Software\Classes\noto", string.Empty)]);
        Assert.Equal(string.Empty, registry.Values[(@"Software\Classes\noto", "URL Protocol")]);
        Assert.Equal($"\"{Executable}\" \"%1\"", registry.Values[(Command, string.Empty)]);
        Assert.Equal(3, registry.Values.Count);
        Assert.All(registry.Values.Keys, key => Assert.StartsWith(@"Software\Classes\noto", key.Key, StringComparison.Ordinal));
    }

    [Fact]
    public void Registering_takes_over_a_scheme_left_by_another_copy()
    {
        var registry = new FakeRegistry();
        registry.Write(Command, string.Empty, "\"C:\\Old\\Noto.exe\" \"%1\"");

        Assert.True(new ProtocolRegistration(Executable, registry).Register());
        Assert.Equal($"\"{Executable}\" \"%1\"", registry.Values[(Command, string.Empty)]);
    }

    [Fact]
    public void Uninstalling_removes_the_scheme_only_when_it_opens_this_copy()
    {
        var registry = new FakeRegistry();
        var registration = new ProtocolRegistration(Executable, registry);
        _ = registration.Register();

        Assert.True(registration.IsThis());
        Assert.True(registration.RemoveIfThis());
        Assert.Empty(registry.Values);
        Assert.False(registration.IsThis());
        Assert.False(registration.RemoveIfThis());
    }

    [Theory]
    [InlineData("\"C:\\Other\\Noto.exe\" \"%1\"")]
    [InlineData("\"C:\\Users\\me\\AppData\\Local\\Noto.App\\current\\Noto.exe\" %1")]
    [InlineData("\"C:\\Users\\me\\AppData\\Local\\Noto.App\\current\\Noto.exe\" \"%1\" --other")]
    [InlineData("calc.exe")]
    public void A_scheme_that_opens_anything_else_is_left_alone(string command)
    {
        var registry = new FakeRegistry();
        registry.Write(Command, string.Empty, command);

        Assert.False(new ProtocolRegistration(Executable, registry).RemoveIfThis());
        Assert.Equal(command, registry.Values[(Command, string.Empty)]);
    }

    [Fact]
    public void The_path_is_compared_ignoring_case_as_windows_does()
    {
        var registry = new FakeRegistry();
        registry.Write(Command, string.Empty, $"\"{Executable.ToUpperInvariant()}\" \"%1\"");

        Assert.True(new ProtocolRegistration(Executable, registry).RemoveIfThis());
    }

    [Fact]
    public void A_registry_that_refuses_is_never_fatal()
    {
        var registry = new FakeRegistry { Refuse = true };
        var registration = new ProtocolRegistration(Executable, registry);

        Assert.False(registration.Register());
        Assert.False(registration.IsThis());
        Assert.False(registration.RemoveIfThis());
    }

    private sealed class FakeRegistry : IUserRegistry
    {
        public Dictionary<(string Key, string Name), string> Values { get; } = [];

        public bool Refuse { get; init; }

        public string? Read(string key, string name)
        {
            Check();
            return Values.GetValueOrDefault((key, name));
        }

        public void Write(string key, string name, string value)
        {
            Check();
            Values[(key, name)] = value;
        }

        public void DeleteTree(string key)
        {
            Check();
            foreach (var entry in Values.Keys.Where(k => k.Key == key || k.Key.StartsWith(key + @"\", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _ = Values.Remove(entry);
            }
        }

        private void Check()
        {
            if (Refuse)
            {
                throw new UnauthorizedAccessException();
            }
        }
    }
}
