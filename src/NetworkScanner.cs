using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace SdeSuporte;

public record IPv4Range(uint First,uint Last,string Label)
{
    public long Count=>(long)Last-First+1;
    public static uint Number(IPAddress ip)=>BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());
    public static IPAddress Address(uint number){byte[] bytes=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(bytes,number);return new IPAddress(bytes);}
    static bool Inside(uint first,uint last,uint network,uint mask)=>((first&mask)==network)&&((last&mask)==network);
    public static IPv4Range Parse(string input,bool allowLoopback=false)
    {
        input=input.Trim();uint first,last;
        uint Read(string s)
        {
            if(!IPAddress.TryParse(s.Trim(),out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork)throw new ArgumentException("Informe uma faixa IPv4, como 10.37.2.0/23 ou 10.37.2.1-10.37.3.254.");
            return Number(ip);
        }
        if(input.Contains('/'))
        {
            var parts=input.Split('/');if(parts.Length!=2||!int.TryParse(parts[1],out int prefix)||prefix is <0 or >32)throw new ArgumentException("Máscara IPv4 inválida.");
            uint ip=Read(parts[0]),mask=prefix==0?0:uint.MaxValue<<(32-prefix);first=ip&mask;last=first|~mask;
            if(prefix<=30){first++;last--;}
        }
        else if(input.Contains('-'))
        {var parts=input.Split('-');if(parts.Length!=2)throw new ArgumentException("Faixa IPv4 inválida.");first=Read(parts[0]);last=Read(parts[1]);}
        else{first=last=Read(input);}
        if(last<first||(long)last-first+1>65536)throw new ArgumentException("Use uma faixa de até 65.536 endereços por varredura.");
        if(!Inside(first,last,0x0A000000,0xFF000000)&&!Inside(first,last,0xAC100000,0xFFF00000)&&!Inside(first,last,0xC0A80000,0xFFFF0000)
            &&!(allowLoopback&&Inside(first,last,0x7F000000,0xFF000000)))throw new ArgumentException("Selecione uma faixa privada da rede interna: 10.x, 172.16–31.x ou 192.168.x.");
        return new(first,last,input);
    }
    public IEnumerable<IPAddress> Addresses(){for(ulong value=First;value<=Last;value++)yield return Address((uint)value);}
    public static string[] LocalCidrs(bool initialScan=false)
    {
        var ranges=new HashSet<string>();
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback))
            foreach(var ip in nic.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork))
            {
                int prefix=ip.PrefixLength;if(initialScan&&prefix<22)prefix=22;
                uint mask=prefix==0?0:uint.MaxValue<<(32-prefix);
                string cidr=$"{Address(Number(ip.Address)&mask)}/{prefix}";
                try{Parse(cidr);ranges.Add(cidr);}catch(ArgumentException){}
            }
        return ranges.ToArray();
    }
}

public record ScanProgress(int Checked,int Total,int Found);

public static class NetworkScanner
{
    public static async Task<Device?> Discover(IPAddress address,int port,string[] roots,string technicianId,CancellationToken cancellation)
    {
        string? certificate=null;
        using var handler=new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,
            ServerCertificateCustomValidationCallback=(_,cert,_,_)=>{
                if(cert is null||DateTime.UtcNow<cert.NotBefore.ToUniversalTime()||DateTime.UtcNow>cert.NotAfter.ToUniversalTime())return false;
                certificate=Convert.ToHexString(SHA256.HashData(cert.RawData));return true;
            }};
        // Bootstrap carries public enrollment metadata only, never credentials or session
        // commands. A device is returned only after BOTH the team signature and the pin
        // of this TLS connection have been verified; normal requests use strict pinning.
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromMilliseconds(1200),MaxResponseContentBufferSize=50000};
        try
        {
            using var response=await http.GetAsync($"https://{address}:{port}/discover",cancellation);
            if(!response.IsSuccessStatusCode)return null;
            var message=JsonSerializer.Deserialize<DiscoveryMessage>(await response.Content.ReadAsByteArrayAsync(cancellation),Json.Options);
            if(message?.Protocol!="SDE-DISCOVERY-1")return null;
            Enrollment.VerifyDevice(message.Enrollment,roots,technicianId);
            if(!string.Equals(certificate,message.Enrollment.Device.CertificateSha256,StringComparison.OrdinalIgnoreCase)||message.Enrollment.Device.Port!=port)return null;
            return message.Enrollment.Device with{Host=address.ToString()};
        }catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){return null;}
        catch(Exception)when(!cancellation.IsCancellationRequested){return null;}
    }
    public static async Task Scan(IEnumerable<IPv4Range> ranges,string[] roots,string technicianId,Action<Device> found,
        IProgress<ScanProgress>? progress=null,CancellationToken cancellation=default,int port=7443)
    {
        var addresses=ranges.SelectMany(r=>r.Addresses()).Distinct().ToArray();
        if(addresses.Length>65536)throw new ArgumentException("Selecione até 65.536 endereços por varredura.");
        int completed=0,discovered=0;
        progress?.Report(new(0,addresses.Length,0));
        await Parallel.ForEachAsync(addresses,new ParallelOptions{MaxDegreeOfParallelism=32,CancellationToken=cancellation},async(address,token)=>{
            var device=await Discover(address,port,roots,technicianId,token);
            if(device is not null){Interlocked.Increment(ref discovered);found(device);}
            int count=Interlocked.Increment(ref completed);if(count%16==0||count==addresses.Length)progress?.Report(new(count,addresses.Length,Volatile.Read(ref discovered)));
        });
    }
}
