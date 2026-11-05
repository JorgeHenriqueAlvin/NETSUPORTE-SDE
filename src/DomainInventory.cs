using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SdeSuporte;

// Directory entries are candidates, never proof of agent identity or permission.
public static class DomainInventory
{
    public static async Task Scan(Action<NetworkHost> found,CancellationToken cancellation)
    {
        const string script="""
        $ErrorActionPreference='Stop'
        [Console]::OutputEncoding=[Text.Encoding]::UTF8
        $root=New-Object DirectoryServices.DirectoryEntry('LDAP://RootDSE')
        $base=$null;$search=$null;$results=$null
        try {
            $dn=[string]$root.Properties['defaultNamingContext'][0]
            if(!$dn){throw 'Dominio Active Directory indisponivel.'}
            $base=New-Object DirectoryServices.DirectoryEntry("LDAP://$dn")
            $search=New-Object DirectoryServices.DirectorySearcher($base)
            $search.Filter='(&(objectCategory=computer)(dNSHostName=*)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))'
            $search.PageSize=500;$search.SizeLimit=10000
            $search.ServerTimeLimit=[TimeSpan]::FromSeconds(30)
            [void]$search.PropertiesToLoad.Add('dnshostname')
            $results=$search.FindAll()
            $names=@($results|ForEach-Object{[string]$_.Properties['dnshostname'][0]})
            ConvertTo-Json -InputObject $names -Compress
        } finally {
            if($results){$results.Dispose()};if($search){$search.Dispose()}
            if($base){$base.Dispose()};$root.Dispose()
        }
        """;
        var start=new ProcessStartInfo("powershell.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Não foi possível consultar o domínio.");
        var output=process.StandardOutput.ReadToEndAsync(cancellation);
        var errors=process.StandardError.ReadToEndAsync(cancellation);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch{try{process.Kill(true);}catch{}throw;}
        if(process.ExitCode!=0)throw new IOException("Consulta ao domínio falhou: "+await errors);
        var hosts=JsonSerializer.Deserialize<string[]>(await output)??[];
        await Parallel.ForEachAsync(hosts.Distinct(StringComparer.OrdinalIgnoreCase),new ParallelOptions{MaxDegreeOfParallelism=16,CancellationToken=cancellation},async(host,token)=>{
            IPAddress[] addresses;
            try{addresses=await Dns.GetHostAddressesAsync(host,token).WaitAsync(TimeSpan.FromSeconds(3),token);}
            catch(Exception)when(!token.IsCancellationRequested){return;}
            foreach(var address in addresses.Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)){
                try{IPv4Range.Parse(address.ToString());}catch(ArgumentException){continue;}
                var result=await NetworkInventory.Probe(address,token);
                if(result is not null)found(result with{Host=host});
            }
        });
    }
}
