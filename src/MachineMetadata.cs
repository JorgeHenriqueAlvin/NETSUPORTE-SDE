namespace SdeSuporte;

public record MachineMetadata(string Alias="",string Description="",string Sector="",string Location="");
public record MachineObservation(string Hostname,string Description,string OS,string Version,string AgentVersion,DateTimeOffset Seen);

public static class MachineNames
{
    public static string Display(Device device,MachineObservation? observation)
    {
        var host=observation?.Hostname;
        if(!string.IsNullOrWhiteSpace(host)&&host!="Nome não identificado")return host.Trim();
        if(!string.IsNullOrWhiteSpace(device.Name))return device.Name.Trim();
        return device.Host;
    }
}
