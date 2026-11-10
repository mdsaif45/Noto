using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>
/// Nothing in this test process clears every SQLite pool. <c>ClearAllPools()</c> disposes the pooled connections of
/// every test class running in parallel: an <see cref="ObjectDisposedException"/> in whichever test is mid-query,
/// or a file found unlocked that should be held. It came back twice after <see cref="TempDatabase.ReleaseOwnPools"/>
/// first removed it. Release a database's own pool instead (<see cref="NotoDatabase.Release"/>).
/// </summary>
public sealed class PoolIsolationTests
{
    [Theory]
    [InlineData(typeof(PoolIsolationTests))]
    [InlineData(typeof(NotoDatabase))]
    public void Nothing_clears_every_pool_in_the_process(Type inAssembly)
    {
        using var pe = new PEReader(File.OpenRead(inAssembly.Assembly.Location));
        MetadataReader md = pe.GetMetadataReader();

        bool callsIt = md.MemberReferences.Select(md.GetMemberReference).Any(m =>
            md.StringComparer.Equals(m.Name, "ClearAllPools")
            && m.Parent.Kind == HandleKind.TypeReference
            && md.GetTypeReference((TypeReferenceHandle)m.Parent) is var type
            && md.StringComparer.Equals(type.Namespace, "Microsoft.Data.Sqlite")
            && md.StringComparer.Equals(type.Name, "SqliteConnection"));

        Assert.False(callsIt, $"{inAssembly.Assembly.GetName().Name} calls SqliteConnection.ClearAllPools(); release the database's own pool instead.");
    }
}
