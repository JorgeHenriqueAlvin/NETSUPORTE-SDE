namespace SdeSuporte;

public record MachineMetadata(string Alias="",string Description="",string Sector="",string Location="");
public record MachineObservation(string Hostname,string Description,string OS,string Version,string AgentVersion,DateTimeOffset Seen,string RamTotal="",string SessionUser="");

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

public static class MachineDescriptions
{
    public static string UserLine(MachineObservation? observation,bool online)
    {
        string user=string.IsNullOrWhiteSpace(observation?.SessionUser)?"não informado":observation.SessionUser;
        return (online?"Usuário conectado: ":"Último usuário informado: ")+user;
    }
    public static string Summary(MachineObservation? observation)
    {
        string os=string.IsNullOrWhiteSpace(observation?.OS)?"Sistema não informado":observation.OS;
        string ram=string.IsNullOrWhiteSpace(observation?.RamTotal)?"não informada":observation.RamTotal;
        return $"{os} • RAM total: {ram}";
    }
    public static MachineObservation ApplyInventory(MachineObservation observation,IReadOnlyDictionary<string,string> inventory)
    {
        string Value(string key,string previous)=>inventory.TryGetValue(key,out var value)&&!string.IsNullOrWhiteSpace(value)?value:previous;
        return observation with {
            Hostname=Value("Nome da máquina / host",observation.Hostname),
            OS=Value("Sistema operacional",observation.OS),
            Version=Value("Versão do sistema",observation.Version),
            Description=Value("Descrição do equipamento",observation.Description),
            RamTotal=Value("RAM total",observation.RamTotal)
        };
    }
}
