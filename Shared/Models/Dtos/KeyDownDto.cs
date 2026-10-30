using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

[DataContract]
public class KeyDownDto
{
    [DataMember(Name = "Code")]
    public string? Code { get; set; }

    [DataMember(Name = "Key")]
    public string Key { get; set; } = string.Empty;
}
