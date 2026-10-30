using MessagePack;
namespace Remotely.Shared.Helpers;
public static class ProtocolSerialization
{
    public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithSecurity(MessagePackSecurity.UntrustedData);
}
