namespace SRNSMudApp.Tests.TestSupport;

[CollectionDefinition(Name)]
public class MsSqlCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "MsSql";
}