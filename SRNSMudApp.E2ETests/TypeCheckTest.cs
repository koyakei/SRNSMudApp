namespace SRNSMudApp.E2ETests;

[TestFixture]
public class TypeCheckTest
{
    [Test]
    public void RegisterComponentExists()
    {
        var type = Type.GetType("SRNSMudApp.Components.Account.Pages.Register, SRNSMudApp");
        Assert.That(type, Is.Not.Null, "Register component not found in SRNSMudApp assembly!");
    }
}