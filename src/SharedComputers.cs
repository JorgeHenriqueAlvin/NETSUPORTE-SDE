using System.Text.Json;

namespace SdeSuporte;

// Only public machine addresses and certificate pins cross this boundary.
// Identity, team credentials and signing keys remain in the Windows account.
public static class SharedComputers
{
    public const int Limit=10000;
    public static Device[] Validate(IEnumerable<Device> values)
    {
        var devices=values.Take(Limit+1).ToArray();
        if(devices.Length>Limit)throw new InvalidDataException("Lista excede 10.000 máquinas.");
        foreach(var device in devices)device.Validate();
        if(devices.Select(d=>d.Id).Distinct().Count()!=devices.Length)throw new InvalidDataException("Lista contém identificações duplicadas.");
        return devices;
    }
    public static Device[] Read(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(stream.Length>8*1024*1024)throw new InvalidDataException("Arquivo de máquinas excede o limite.");
        return Validate(JsonSerializer.Deserialize<Device[]>(stream,Json.Options)??throw new InvalidDataException("Lista vazia ou inválida."));
    }
    public static Device[] Exchange(string directory,string team,string publisher,Device[] local)
    {
        if(team.Length!=64||!team.All(Uri.IsHexDigit)||!Guid.TryParse(publisher,out _))throw new InvalidDataException("Configuração de sincronização inválida.");
        var root=Path.Combine(directory,"SDE-Computadores",team);
        Directory.CreateDirectory(root);
        Json.Write(Path.Combine(root,publisher+".json"),Validate(local));
        var merged=new Dictionary<string,Device>(StringComparer.Ordinal);
        foreach(var path in Directory.EnumerateFiles(root,"*.json").Order(StringComparer.Ordinal).Take(100))
        {
            try
            {
                foreach(var device in Read(path))
                {
                    // Conflicting certificate pins are never silently replaced.
                    if(merged.TryGetValue(device.Id,out var previous)&&previous.CertificateSha256!=device.CertificateSha256)continue;
                    if(merged.Count<Limit||merged.ContainsKey(device.Id))merged[device.Id]=device;
                }
            }
            catch(Exception e)when(e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException){}
        }
        return merged.Values.ToArray();
    }
}
