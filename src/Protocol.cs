using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SdeSuporte;
public record TechnicianKey(string Id, string Name, string PublicKey, string[]? DiscoveryAddresses = null);
public record Device(string Id, string Name, string Sector, string Host, int Port, string CertificateSha256)
{
    public override string ToString() => $"{Name} • {Sector} ({Host})";
    public void Validate()
    {
        if (!Guid.TryParse(Id,out _) || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Sector)
            || string.IsNullOrWhiteSpace(Host) || Uri.CheckHostName(Host) == UriHostNameType.Unknown
            || Host.Contains(':') || Port is < 1024 or > 65535 || CertificateSha256.Length != 64
            || !CertificateSha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Arquivo de computador inválido.");
    }
}
public record AgentConfig(Device Device, TechnicianKey[] Technicians, bool RequireConsent = true, DeviceEnrollment? Enrollment = null);
public record RemoteSession(string Id, string TechnicianId, string TechnicianName, DateTimeOffset Started, DateTimeOffset Expires);
public record RemoteMonitor(string Id,string Name,int X,int Y,int Width,int Height,bool Primary)
{
    public override string ToString()=>Name;
}
public static class MonitorCoordinates
{
    public static RemoteMonitor[] Inventory(Screen[] displays)=>displays.Select((s,i)=>new RemoteMonitor(s.DeviceName,$"Monitor {i+1}"+(s.Primary?" • Principal":"")+$" ({s.Bounds.Width} × {s.Bounds.Height})",s.Bounds.X,s.Bounds.Y,s.Bounds.Width,s.Bounds.Height,s.Primary)).ToArray();
    public static string Layout(Screen[] displays)=>Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Inventory(displays),Json.Options));
    public static Point Offset(Point imagePoint,int offsetX,int offsetY)=>new(checked(imagePoint.X+offsetX),checked(imagePoint.Y+offsetY));
}
public record InputCommand(string Kind, int X=0, int Y=0, string Text="", int Key=0, int Delta=0,string Layout="");
public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented=true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path),Options) ?? throw new InvalidDataException("Arquivo vazio.");
    public static void Write<T>(string path,T value) => File.WriteAllText(path,JsonSerializer.Serialize(value,Options),new UTF8Encoding(false));
}
public sealed class TechnicianIdentity : IDisposable
{
    readonly ECDsa key;
    readonly object keyLock = new();
    public TechnicianKey Public { get; }
    public TeamCredential? Credential {get;set;}
    public TechnicianIdentity(ECDsa key,string name)
    {
        this.key=key;
        if (string.IsNullOrWhiteSpace(name) || name.Length>80 || name.Any(char.IsControl)) throw new ArgumentException("Informe o nome do técnico (até 80 caracteres).");
        var pub=key.ExportSubjectPublicKeyInfo();
        Public=new(Convert.ToHexString(SHA256.HashData(pub)),name.Trim(),Convert.ToBase64String(pub),DiscoveryNetwork.Addresses());
    }
    public byte[] ExportPrivate() => key.ExportPkcs8PrivateKey();
    public static TechnicianIdentity FromPrivate(byte[] bytes,string name) { var k=ECDsa.Create(); k.ImportPkcs8PrivateKey(bytes,out _); return new(k,name); }
    public string SignDetached(byte[] data){lock(keyLock)return Convert.ToBase64String(key.SignData(data,HashAlgorithmName.SHA256));}
    public string Sign(string method,string path,byte[] body,string session,string time,string nonce,string device) => SignDetached(Canonical(method,path,body,session,time,nonce,device));
    public static byte[] Canonical(string method,string path,byte[] body,string session,string time,string nonce,string device) => Encoding.UTF8.GetBytes(
        $"{device}\n{method}\n{path}\n{Convert.ToHexString(SHA256.HashData(body))}\n{session}\n{time}\n{nonce}");
    public void Dispose() => key.Dispose();
}
public sealed class RequestVerifier
{
    readonly Dictionary<string,TechnicianKey> allowed;
    readonly string device;
    readonly TechnicianKey? teamRoot;
    readonly ConcurrentDictionary<string,long> seen=new();
    public RequestVerifier(IEnumerable<TechnicianKey> technicians,string device,TechnicianKey? teamRoot=null)
    {
        allowed=new(StringComparer.Ordinal);this.device=device;this.teamRoot=teamRoot;
        if(teamRoot is not null)Enrollment.VerifyPublic(teamRoot);
        foreach(var t in technicians)
        {
            var pub=Convert.FromBase64String(t.PublicKey);
            using var k=ECDsa.Create(); k.ImportSubjectPublicKeyInfo(pub,out var read);
            if(read!=pub.Length || k.KeySize!=256 || Convert.ToHexString(SHA256.HashData(pub))!=t.Id || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length>80 || t.Name.Any(char.IsControl))
                throw new InvalidDataException("Identificação do técnico inválida.");
            allowed.Add(t.Id,t);
        }
        if(allowed.Count==0) throw new InvalidDataException("Autorize pelo menos um técnico na instalação.");
    }
    public TechnicianKey? Verify(string id,string deviceId,string method,string path,byte[] body,string session,string time,string nonce,string signature,string credential="")
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        allowed.TryGetValue(id,out var t);
        if(t is null&&teamRoot is not null)t=TeamAccess.Verify(TeamAccess.Decode(credential),teamRoot);
        if(deviceId!=device || t is null || t.Id!=id || !long.TryParse(time,out var stamp) || stamp<now-45 || stamp>now+45
            || nonce.Length!=32 || !nonce.All(Uri.IsHexDigit) || signature.Length>256) return null;
        try
        {
            using var k=ECDsa.Create(); k.ImportSubjectPublicKeyInfo(Convert.FromBase64String(t.PublicKey),out _);
            if(!k.VerifyData(TechnicianIdentity.Canonical(method,path,body,session,time,nonce,device),Convert.FromBase64String(signature),HashAlgorithmName.SHA256)) return null;
            foreach(var entry in seen) if(entry.Value<now-90) seen.TryRemove(entry.Key,out _);
            if(seen.Count>20000 || !seen.TryAdd(id+nonce,now)) return null;
            return t;
        } catch(CryptographicException) { return null; } catch(FormatException) { return null; }
    }
}
public sealed class RemoteClient : IDisposable
{
    readonly HttpClient http;
    readonly TechnicianIdentity identity;
    readonly string deviceId;
    public RemoteSession? Session {get;private set;}
    public RemoteClient(Device device,TechnicianIdentity identity,TimeSpan? timeout=null)
    {
        device.Validate(); this.identity=identity;deviceId=device.Id;
        http=new(new HttpClientHandler {
            UseProxy=false,AllowAutoRedirect=false,
            ServerCertificateCustomValidationCallback=(_,cert,_,_) => cert is not null
                && DateTime.UtcNow>=cert.NotBefore.ToUniversalTime() && DateTime.UtcNow<=cert.NotAfter.ToUniversalTime()
                && string.Equals(Convert.ToHexString(SHA256.HashData(cert.RawData)),device.CertificateSha256,StringComparison.OrdinalIgnoreCase)
        }) { BaseAddress=new Uri($"https://{device.Host}:{device.Port}"),Timeout=timeout??TimeSpan.FromSeconds(40) };
    }
    public async Task<HttpResponseMessage> Request(HttpMethod method,string path,object? value=null)
    {
        for(int attempt=0;;attempt++)
        {
            try{return await RequestOnce(method,path,value);}
            catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.TooManyRequests&&attempt<3){await Task.Delay(150);}
        }
    }
    async Task<HttpResponseMessage> RequestOnce(HttpMethod method,string path,object? value)
    {
        byte[] body=value is null ? [] : JsonSerializer.SerializeToUtf8Bytes(value,Json.Options);
        string time=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        string nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string session=Session?.Id??"";
        using var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-Device",deviceId);request.Headers.Add("X-Technician",identity.Public.Id); request.Headers.Add("X-Time",time); request.Headers.Add("X-Nonce",nonce);
        request.Headers.Add("X-Session",session);
        if(identity.Credential is not null)request.Headers.Add("X-Team-Credential",TeamAccess.Encode(identity.Credential));
        request.Headers.Add("X-Signature",identity.Sign(method.Method,path,body,session,time,nonce,deviceId));
        if(value is not null) { request.Content=new ByteArrayContent(body); request.Content.Headers.ContentType=new("application/json"); }
        var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode)
        {
            string message=await response.Content.ReadAsStringAsync(); var status=response.StatusCode; response.Dispose();
            throw new HttpRequestException($"HTTP {(int)status}: {message}",null,status);
        }
        return response;
    }
    public async Task Start() { using var r=await Request(HttpMethod.Post,"/session"); Session=JsonSerializer.Deserialize<RemoteSession>(await r.Content.ReadAsStringAsync(),Json.Options)??throw new Exception("Sessão inválida."); }
    public async Task Stop() { try {if(Session is not null) {using var r=await Request(HttpMethod.Post,"/session/end");}}finally{Session=null;} }
    public void Dispose()=>http.Dispose();
}
