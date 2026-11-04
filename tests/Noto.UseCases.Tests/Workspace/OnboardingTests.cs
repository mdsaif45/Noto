using Noto.Core.Settings;
using Noto.Core.Storage;
using Noto.Core.Workspace;
using Noto.UseCases.Workspace;
using Xunit;

namespace Noto.UseCases.Tests.Workspace;

/// <summary>
/// When a workspace opens with its welcome, and what ending it records (ADR-021). The rule against the store's
/// contract in memory; the same rule across real starts of a real data root is in Noto.Infrastructure.Tests.
/// </summary>
public sealed class OnboardingTests
{
    private static readonly Func<bool> Empty = () => false;
    private static readonly Func<bool> Holding = () => true;

    [Fact]
    public void A_workspace_created_now_and_empty_opens_with_the_welcome_and_owes_it()
    {
        var store = new FakeSettings();
        var onboarding = new Onboarding(store);

        Assert.True(onboarding.ShowAtStart(created: true, restored: false, Empty));
        Assert.Equal(WelcomeState.Due, onboarding.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WelcomeState.None)]
    [InlineData(WelcomeState.Completed)]
    [InlineData(WelcomeState.Skipped)]
    public void A_workspace_that_existed_never_opens_with_it_and_nothing_is_written(WelcomeState? stored)
    {
        var store = new FakeSettings();
        if (stored is { } state)
        {
            store.Seed(SettingKeys.OnboardingWelcome.Name, state);
        }

        bool asked = false;
        Assert.False(new Onboarding(store).ShowAtStart(created: false, restored: false, () => asked = true));
        Assert.Empty(store.Writes);
        Assert.False(asked);
    }

    [Fact]
    public void A_welcome_still_owed_opens_again_at_the_next_start()
    {
        // Noto ended, or crashed, with the welcome open: nothing recorded an end.
        var store = new FakeSettings();
        _ = new Onboarding(store).ShowAtStart(created: true, restored: false, Empty);

        Assert.True(new Onboarding(store).ShowAtStart(created: false, restored: false, Empty));
        Assert.Equal(WelcomeState.Due, new Onboarding(store).State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_workspace_that_holds_anything_is_not_shown_it(bool created)
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.OnboardingWelcome.Name, WelcomeState.Due);

        Assert.False(new Onboarding(store).ShowAtStart(created, restored: false, Holding));
    }

    [Fact]
    public void A_workspace_that_cannot_say_what_it_holds_is_not_shown_it_and_startup_goes_on()
    {
        var store = new FakeSettings();

        Assert.False(new Onboarding(store).ShowAtStart(
            created: true,
            restored: false,
            () => throw new StorageException(StorageFailure.Corrupt, "Simulated failure.")));
    }

    [Theory]
    [InlineData(WelcomeState.Due, WelcomeState.None)]
    [InlineData(WelcomeState.Completed, WelcomeState.Completed)]
    [InlineData(WelcomeState.Skipped, WelcomeState.Skipped)]
    [InlineData(WelcomeState.None, WelcomeState.None)]
    public void A_restored_workspace_never_opens_with_it_and_owes_nothing_after(WelcomeState carried, WelcomeState after)
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.OnboardingWelcome.Name, carried);
        var onboarding = new Onboarding(store);

        Assert.False(onboarding.ShowAtStart(created: false, restored: true, Empty));
        Assert.Equal(after, onboarding.State);
        Assert.False(new Onboarding(store).ShowAtStart(created: false, restored: false, Empty));
    }

    [Fact]
    public void A_restore_wins_even_over_a_created_database()
    {
        var store = new FakeSettings();

        Assert.False(new Onboarding(store).ShowAtStart(created: true, restored: true, Empty));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void Finishing_records_completed_and_it_does_not_open_again()
    {
        var store = new FakeSettings();
        var onboarding = new Onboarding(store);
        _ = onboarding.ShowAtStart(created: true, restored: false, Empty);

        Assert.True(onboarding.Complete());
        Assert.Equal(WelcomeState.Completed, onboarding.State);
        Assert.False(new Onboarding(store).ShowAtStart(created: false, restored: false, Empty));
    }

    [Fact]
    public void Skipping_records_skipped_and_it_does_not_interrupt_startup_again()
    {
        var store = new FakeSettings();
        var onboarding = new Onboarding(store);
        _ = onboarding.ShowAtStart(created: true, restored: false, Empty);

        Assert.True(onboarding.Skip());
        Assert.Equal(WelcomeState.Skipped, onboarding.State);
        for (int start = 0; start < 3; start++)
        {
            Assert.False(new Onboarding(store).ShowAtStart(created: false, restored: false, Empty));
        }
    }

    [Fact]
    public void A_failed_write_never_stops_startup_and_the_new_workspace_still_opens_with_it()
    {
        var store = new FakeSettings { FailWritesTo = SettingKeys.OnboardingWelcome.Name };
        var onboarding = new Onboarding(store);

        Assert.True(onboarding.ShowAtStart(created: true, restored: false, Empty));
        Assert.False(onboarding.Complete());
        Assert.False(onboarding.Skip());
        Assert.Equal(WelcomeState.None, onboarding.State);
    }

    [Fact]
    public void A_failed_settle_of_a_restored_workspace_still_does_not_show_it()
    {
        var store = new FakeSettings { FailWritesTo = SettingKeys.OnboardingWelcome.Name };
        store.Seed(SettingKeys.OnboardingWelcome.Name, WelcomeState.Due);

        Assert.False(new Onboarding(store).ShowAtStart(created: false, restored: true, Empty));
    }

    [Fact]
    public void A_stored_value_that_is_not_a_state_reads_as_none()
    {
        var store = new FakeSettings();
        store.Seed(SettingKeys.OnboardingWelcome.Name, (WelcomeState)42);

        Assert.Equal(WelcomeState.None, new Onboarding(store).State);
        Assert.False(new Onboarding(store).ShowAtStart(created: false, restored: false, Empty));
        Assert.False(store.Write(SettingKeys.OnboardingWelcome, (WelcomeState)42));
    }
}
