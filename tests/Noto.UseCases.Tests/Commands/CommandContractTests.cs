using System.Reflection;
using Noto.Core.Commands;
using Noto.UseCases.Commands;
using Xunit;

namespace Noto.UseCases.Tests.Commands;

/// <summary>
/// #21: every command declares, as <see cref="ICommand{TResult}"/>, exactly the result its handler returns, so any
/// command can be registered and dispatched, and the dispatcher's result type is the handler's own.
/// </summary>
public sealed class CommandContractTests
{
    [Fact]
    public void Every_handlers_command_declares_the_result_its_handler_returns()
    {
        var handles = typeof(AssemblyMarker).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "Handle" && m.GetParameters().Length == 1 && typeof(ICommandResult).IsAssignableFrom(m.ReturnType))
            .ToList();

        var undeclared = handles
            .Where(m => !typeof(ICommand<>).MakeGenericType(m.ReturnType).IsAssignableFrom(m.GetParameters()[0].ParameterType))
            .Select(m => $"{m.GetParameters()[0].ParameterType.Name} -> {m.ReturnType.Name}")
            .ToList();

        // 23, plus P1.1's PurgeNote, RestoreNoteToFolder, PurgeFolder and EmptyRecycleBin.
        Assert.Equal(27, handles.Count);
        Assert.True(undeclared.Count == 0, "Commands not declaring their handler's result: " + string.Join(", ", undeclared));
    }
}
