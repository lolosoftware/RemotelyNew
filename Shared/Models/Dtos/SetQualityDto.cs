using System.Runtime.Serialization;
namespace Remotely.Shared.Models.Dtos;
[DataContract]
public class SetQualityDto
{
    // Zero selects adaptive quality; other values are clamped by the agent.
    [DataMember(Name = "Quality")]
    public int Quality { get; set; }
}
