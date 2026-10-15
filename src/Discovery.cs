using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
namespace SdeSuporte;
public record DiscoveryRequest(string Id,long Time,string Nonce,string Signature);
public record DiscoveryMessage(string Protocol,DeviceEnrollment Enrollment);
public static class DiscoveryNetwork
{
    public const int ConsolePort=7445,AgentPort=7446;
    public static string[] Addresses()
    {
        try{return NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback)
            .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a.Address)).Select(a=>a.Address.ToString()).Append(Dns.GetHostName()).Distinct().ToArray();}
        catch{return [Dns.GetHostName()];}
    }
    public static IEnumerable<IPEndPoint> Broadcasts()
    {
        var addresses=new HashSet<string>{"255.255.255.255"};
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up))
            foreach(var ip in nic.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork))
            {var b=ip.Address.GetAddressBytes();var mask=ip.IPv4Mask.GetAddressBytes();for(int i=0;i<4;i++)b[i]|=(byte)~mask[i];addresses.Add(new IPAddress(b).ToString());}
        return addresses.Select(x=>new IPEndPoint(IPAddress.Parse(x),AgentPort));
    }
    public static byte[] RequestBytes(string id,long time,string nonce)=>System.Text.Encoding.UTF8.GetBytes($"SDE-DISCOVER-1\n{id}\n{time}\n{nonce}");
    public static bool VerifyRequest(DiscoveryRequest request,TechnicianKey[] technicians)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();var t=technicians.FirstOrDefault(t=>t.Id==request.Id);
        if(t is null||request.Time<now-45||request.Time>now+45||request.Nonce.Length!=32)return false;
        try{using var key=ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(t.PublicKey),out _);return key.VerifyData(RequestBytes(request.Id,request.Time,request.Nonce),Convert.FromBase64String(request.Signature),HashAlgorithmName.SHA256);}catch{return false;}
    }
}
public sealed class AgentDiscovery:IDisposable
{
    readonly AgentConfig config;readonly UdpClient udp;readonly CancellationTokenSource stop=new();
    public AgentDiscovery(AgentConfig config)
    {
        this.config=config;
        if(config.Enrollment is null)throw new InvalidDataException("Agente sem credencial de cadastro automático.");
        Enrollment.VerifyDevice(config.Enrollment,[config.Enrollment.Approval.Permission.Issuer.Id],config.Technicians[0].Id);
        if(config.Enrollment.Device!=config.Device||!config.Technicians.Select(t=>t.Id+"|"+t.Name+"|"+t.PublicKey).Order().SequenceEqual(config.Enrollment.Approval.Permission.Technicians.Select(t=>t.Id+"|"+t.Name+"|"+t.PublicKey).Order()))throw new InvalidDataException("Política do agente divergente do instalador autorizado.");
        udp=new UdpClient(new IPEndPoint(IPAddress.Any,DiscoveryNetwork.AgentPort));_=Task.Run(Receive);_=Task.Run(Announce);
    }
    byte[] Message()=>Enrollment.Bytes(new DiscoveryMessage("SDE-DISCOVERY-1",config.Enrollment!));
    async Task Receive()
    {
        while(!stop.IsCancellationRequested)
        {
            try{var data=await udp.ReceiveAsync(stop.Token);if(data.Buffer.Length>2048)continue;
                var request=JsonSerializer.Deserialize<DiscoveryRequest>(data.Buffer,Json.Options);if(request is null||!DiscoveryNetwork.VerifyRequest(request,config.Technicians))continue;
                await udp.SendAsync(Message(),data.RemoteEndPoint,stop.Token);
            }catch(OperationCanceledException){break;}catch(ObjectDisposedException){break;}catch{await Task.Delay(200);}
        }
    }
    async Task Announce()
    {
        while(!stop.IsCancellationRequested)
        {
            foreach(var host in config.Technicians.SelectMany(t=>t.DiscoveryAddresses??[]).Distinct().Take(100))
            {try{foreach(var ip in (await Dns.GetHostAddressesAsync(host,stop.Token)).Where(x=>x.AddressFamily==AddressFamily.InterNetwork))await udp.SendAsync(Message(),new IPEndPoint(ip,DiscoveryNetwork.ConsolePort),stop.Token);}catch{}}
            try{await Task.Delay(TimeSpan.FromSeconds(8),stop.Token);}catch(OperationCanceledException){break;}
        }
    }
    public void Dispose(){stop.Cancel();udp.Dispose();}
}
public sealed class ConsoleDiscovery:IDisposable
{
    readonly TechnicianIdentity identity;readonly Func<string[]> roots;readonly Action<Device> found;
    readonly UdpClient udp;readonly CancellationTokenSource stop=new();
    public ConsoleDiscovery(TechnicianIdentity identity,Func<string[]> roots,Action<Device> found,int port=DiscoveryNetwork.ConsolePort)
    {
        this.identity=identity;this.roots=roots;this.found=found;udp=new(new IPEndPoint(IPAddress.Any,port));udp.EnableBroadcast=true;
        _=Task.Run(Receive);_=Task.Run(SearchLoop);
    }
    async Task SearchLoop()
    {
        while(!stop.IsCancellationRequested){await Search();try{await Task.Delay(5000,stop.Token);}catch(OperationCanceledException){break;}}
    }
    public async Task Search()
    {
        long time=DateTimeOffset.UtcNow.ToUnixTimeSeconds();string nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var request=new DiscoveryRequest(identity.Public.Id,time,nonce,identity.SignDetached(DiscoveryNetwork.RequestBytes(identity.Public.Id,time,nonce)));
        var bytes=Enrollment.Bytes(request);
        foreach(var endpoint in DiscoveryNetwork.Broadcasts()){try{await udp.SendAsync(bytes,endpoint,stop.Token);}catch{}}
    }
    public async Task SearchTo(IPEndPoint endpoint)
    {
        long time=DateTimeOffset.UtcNow.ToUnixTimeSeconds();string nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var request=new DiscoveryRequest(identity.Public.Id,time,nonce,identity.SignDetached(DiscoveryNetwork.RequestBytes(identity.Public.Id,time,nonce)));
        await udp.SendAsync(Enrollment.Bytes(request),endpoint,stop.Token);
    }
    async Task Receive()
    {
        while(!stop.IsCancellationRequested)
        {
            try{var message=await udp.ReceiveAsync(stop.Token);if(message.Buffer.Length>50000)continue;
                var data=JsonSerializer.Deserialize<DiscoveryMessage>(message.Buffer,Json.Options);if(data?.Protocol!="SDE-DISCOVERY-1")continue;
                Enrollment.VerifyDevice(data.Enrollment,roots(),identity.Public.Id);
                found(data.Enrollment.Device with{Host=message.RemoteEndPoint.Address.ToString()});
            }catch(OperationCanceledException){break;}catch(ObjectDisposedException){break;}catch{}
        }
    }
    public void Dispose(){stop.Cancel();udp.Dispose();}
}
