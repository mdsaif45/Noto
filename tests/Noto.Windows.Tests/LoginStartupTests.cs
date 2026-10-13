using Microsoft.Win32;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Run at login (#94, ADR-015): the registered command, Windows' Startup apps
/// setting as read, and the state and tray row they give (deterministic), and
/// the registration against a real registry key (runtime).
/// </summary>
/// <remarks>
/// The runtime tests never touch the real <c>Run</c> or
/// <c>StartupApproved</c> keys: each uses its own scratch key under
/// <c>HKCU\Software\Noto.Tests</c>, removed afterwards. Writing a scratch
/// <c>StartupApproved</c> value stands in for Windows; Noto itself never
/// writes one, which these tests check.
/// </remarks>
public sealed class LoginStartupTests : IDisposable
{
    private const string Scratch = @"Software\Noto.Tests";
    private const string Exe = @"C:\Program Files\Noto\Noto.exe";

    private readonly string _root = $@"{Scratch}\{Guid.NewGuid():N}";

    private string RunKey => _root + @"\Run";

    private string ApprovedKey => _root + @"\Approved";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

        using RegistryKey? scratch = Registry.CurrentUser.OpenSubKey(Scratch);

        if (scratch is not null && scratch.SubKeyCount == 0 && scratch.ValueCount == 0)
        {
            scratch.Dispose();
            Registry.CurrentUser.DeleteSubKey(Scratch, throwOnMissingSubKey: false);
        }
    }

    // ------------------------------------------------ the command (deterministic)

    [Fact]
    public void The_command_is_the_quoted_executable_and_the_login_switch_only()
    {
        Assert.Equal("\"C:\\Program Files\\Noto\\Noto.exe\" --login", LoginRegistration.CommandFor(Exe));
        Assert.DoesNotContain("--data-root", LoginRegistration.CommandFor(Exe), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Noto\\Noto.exe\" --login", true)]
    [InlineData("\"c:\\program files\\noto\\NOTO.EXE\" --login", true)]
    [InlineData("  \"C:\\Program Files\\Noto\\Noto.exe\"   --login  ", true)]
    [InlineData("C:\\Noto\\Noto.exe --login", false)]
    [InlineData("\"C:\\Program Files\\Noto\\Noto.exe\"", false)]
    [InlineData("\"C:\\Program Files\\Noto\\Noto.exe\" --login --data-root=C:\\x", false)]
    [InlineData("\"C:\\Program Files\\Noto\\Noto.exe\" --LOGIN", false)]
    [InlineData("\"C:\\Program Files\\Other\\Noto.exe\" --login", false)]
    [InlineData("\"C:\\Program Files\\Noto\\Noto.exe --login", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_registration_points_at_this_Noto_only_with_exactly_its_command(string? command, bool expected)
    {
        Assert.Equal(expected, LoginRegistration.PointsAt(command, Exe));
    }

    [Fact]
    public void An_unquoted_path_without_spaces_is_understood()
    {
        Assert.True(LoginRegistration.PointsAt(@"C:\Noto\Noto.exe --login", @"C:\Noto\Noto.exe"));
    }

    // ------------------------------------- Startup apps, as read (deterministic)

    [Fact]
    public void The_measured_forms_read_as_enabled_and_disabled()
    {
        Assert.Equal(StartupApproval.Enabled, LoginRegistration.Interpret(Approval(0x02)));
        Assert.Equal(StartupApproval.Disabled, LoginRegistration.Interpret(Approval(0x03)));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x06)]
    [InlineData(0x07)]
    [InlineData(0xFF)]
    public void An_unrecognised_first_byte_is_unreadable(byte first)
    {
        Assert.Equal(StartupApproval.Unreadable, LoginRegistration.Interpret(Approval(first)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(13)]
    public void Any_other_length_is_unreadable(int length)
    {
        var data = new byte[length];

        if (length > 0)
        {
            data[0] = 0x02;
        }

        Assert.Equal(StartupApproval.Unreadable, LoginRegistration.Interpret(data));
        Assert.Equal(StartupApproval.Unreadable, LoginRegistration.Interpret(null));
    }

    [Theory]
    [InlineData((int)StartupApproval.Enabled, LoginStartupState.On)]
    [InlineData((int)StartupApproval.Disabled, LoginStartupState.WindowsDisabled)]
    [InlineData((int)StartupApproval.Unreadable, LoginStartupState.WindowsDisabled)]
    public void Only_an_enabled_registration_is_on(int approval, LoginStartupState expected)
    {
        Assert.Equal(expected, LoginRegistration.Classify((StartupApproval)approval));
    }

    [Fact]
    public void The_tray_row_is_honest_for_every_state()
    {
        Assert.Equal(new LoginStartupRow(Enabled: true, Checked: false, TurnedOffInWindows: false), LoginStartupRow.For(LoginStartupState.Off));
        Assert.Equal(new LoginStartupRow(Enabled: true, Checked: true, TurnedOffInWindows: false), LoginStartupRow.For(LoginStartupState.On));
        Assert.Equal(new LoginStartupRow(Enabled: false, Checked: false, TurnedOffInWindows: true), LoginStartupRow.For(LoginStartupState.WindowsDisabled));
    }

    // ------------------------------------------ the registration (runtime, scratch)

    [Fact]
    public void Nothing_registered_is_off()
    {
        Assert.Equal(LoginStartupState.Off, Registration().State());
    }

    [Fact]
    public void Enabling_writes_the_command_as_a_string_and_reads_on()
    {
        LoginRegistration registration = Registration();

        Assert.Equal(LoginStartupState.On, registration.Enable());
        Assert.Equal(LoginStartupState.On, registration.State());

        using RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey)!;
        Assert.Equal(RegistryValueKind.String, run.GetValueKind("Noto"));
        Assert.Equal(LoginRegistration.CommandFor(Exe), run.GetValue("Noto"));
        Assert.Single(run.GetValueNames());
    }

    [Fact]
    public void Disabling_removes_the_value_and_reads_off_and_is_idempotent()
    {
        LoginRegistration registration = Registration();
        _ = registration.Enable();

        Assert.Equal(LoginStartupState.Off, registration.Disable());
        Assert.Equal(LoginStartupState.Off, registration.Disable());

        using RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey)!;
        Assert.Empty(run.GetValueNames());
    }

    [Fact]
    public void A_registration_for_another_executable_is_off_and_enabling_points_it_here()
    {
        WriteRun("\"C:\\Elsewhere\\Noto.exe\" --login", RegistryValueKind.String);
        LoginRegistration registration = Registration();

        Assert.Equal(LoginStartupState.Off, registration.State());
        Assert.Equal(LoginStartupState.On, registration.Enable());
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_off_and_enabling_replaces_it()
    {
        WriteRun(42, RegistryValueKind.DWord);
        LoginRegistration registration = Registration();

        Assert.Equal(LoginStartupState.Off, registration.State());
        Assert.Equal(LoginStartupState.On, registration.Enable());
    }

    [Theory]
    [InlineData(0x02, LoginStartupState.On)]
    [InlineData(0x03, LoginStartupState.WindowsDisabled)]
    [InlineData(0x07, LoginStartupState.WindowsDisabled)]
    public void Windows_startup_apps_decides_between_on_and_windows_disabled(byte first, LoginStartupState expected)
    {
        WriteApproval(Approval(first), RegistryValueKind.Binary);
        LoginRegistration registration = Registration();

        Assert.Equal(expected, registration.Enable());
        Assert.Equal(expected, registration.State());
    }

    [Fact]
    public void An_approval_of_the_wrong_kind_is_windows_disabled()
    {
        WriteApproval("enabled", RegistryValueKind.String);

        Assert.Equal(LoginStartupState.WindowsDisabled, Registration().Enable());
    }

    [Fact]
    public void Enabling_while_windows_has_turned_it_off_does_not_claim_on_and_never_touches_startup_apps()
    {
        byte[] disabled = Approval(0x03);
        WriteApproval(disabled, RegistryValueKind.Binary);
        LoginRegistration registration = Registration();

        Assert.Equal(LoginStartupState.WindowsDisabled, registration.Enable());
        Assert.Equal(LoginStartupState.Off, registration.Disable());
        Assert.Equal(LoginStartupState.WindowsDisabled, registration.Enable());

        using RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey)!;
        Assert.Equal(["Noto"], approved.GetValueNames());
        Assert.Equal(disabled, (byte[])approved.GetValue("Noto")!);
    }

    [Fact]
    public void No_operation_creates_a_startup_apps_entry()
    {
        LoginRegistration registration = Registration();

        _ = registration.Enable();
        _ = registration.State();
        _ = registration.Disable();

        Assert.Null(Registry.CurrentUser.OpenSubKey(ApprovedKey));
    }

    private LoginRegistration Registration() => new(Exe, RunKey, ApprovedKey);

    private void WriteRun(object value, RegistryValueKind kind)
    {
        using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey);
        run.SetValue("Noto", value, kind);
    }

    private void WriteApproval(object value, RegistryValueKind kind)
    {
        using RegistryKey approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        approved.SetValue("Noto", value, kind);
    }

    private static byte[] Approval(byte first)
    {
        var data = new byte[12];
        data[0] = first;
        return data;
    }
}
