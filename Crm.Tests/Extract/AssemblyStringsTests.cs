using System.Text;
using Crm.Extract.Metadata;
using Xunit;

namespace Crm.Tests.Extract;

/// <summary>
/// Reading the addresses out of a registered assembly. A .NET assembly keeps its string constants as UTF-16, so a
/// scan that only looks at bytes finds nothing but single letters — which is the mistake this guards against.
/// </summary>
public sealed class AssemblyStringsTests
{
    [Fact]
    public void An_Address_Is_Found_In_A_Utf16_String_Constant()
    {
        byte[] assembly = Encoding.Unicode.GetBytes("PolicyNumber\0https://nova.ornek.local/imza/v2\0System.Runtime");

        Assert.Equal(["https://nova.ornek.local/imza/v2"], AssemblyStrings.Addresses(assembly));
    }

    [Fact]
    public void An_Address_Is_Found_In_A_Plain_Byte_Run()
    {
        byte[] assembly = Encoding.ASCII.GetBytes("<endpoint address=\"net.tcp://kuyruk.local:808/Service\" />");

        Assert.Equal(["net.tcp://kuyruk.local:808/Service"], AssemblyStrings.Addresses(assembly));
    }

    /// <summary>Every .NET assembly carries these; none of them is an endpoint, and they would drown the report.</summary>
    [Fact]
    public void Framework_Namespaces_Are_Not_Addresses()
    {
        byte[] assembly = Encoding.Unicode.GetBytes(
            "http://schemas.microsoft.com/netfx/2009/xaml http://www.w3.org/2001/XMLSchema http://tempuri.org/");

        Assert.Empty(AssemblyStrings.Addresses(assembly));
    }

    [Fact]
    public void Bytes_That_Are_Not_An_Assembly_Yield_Nothing()
    {
        Assert.Empty(AssemblyStrings.Addresses([0, 1, 2, 3, 255]));
    }
}
