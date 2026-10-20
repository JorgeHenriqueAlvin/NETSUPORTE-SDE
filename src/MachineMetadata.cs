namespace SdeSuporte;

public record MachineMetadata(string Alias="",string Description="",string Sector="",string Location="");
public record MachineObservation(string Hostname,string Description,string OS,string Version,string AgentVersion,DateTimeOffset Seen);
