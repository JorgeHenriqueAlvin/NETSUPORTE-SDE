using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SdeSuporte;

public static class MachineInventory
{
    public static Dictionary<string,string> Collect()
    {
        var data=new Dictionary<string,string>
        {
            ["Nome da máquina / host"]=Environment.MachineName,
            ["Sistema operacional"]=RuntimeInformation.OSDescription,
            ["Versão do sistema"]=Environment.OSVersion.VersionString,
            ["Arquitetura"]=RuntimeInformation.OSArchitecture.ToString(),
            ["Processadores lógicos"]=Environment.ProcessorCount.ToString(),
            ["Tempo ligado"]=TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\d\ hh\h\ mm\m"),
            ["Atualizado em"]=DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")
        };
        void Read(Action action){try{action();}catch{/* Unsupported or inaccessible fields remain unavailable. */}}
        if(OperatingSystem.IsWindows())
        {
            Read(()=>{
                using var os=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                string build=os?.GetValue("CurrentBuildNumber")?.ToString()??"";
                string product=os?.GetValue("ProductName")?.ToString()??RuntimeInformation.OSDescription;
                if(int.TryParse(build,out int number)&&number>=22000&&product.Contains("Windows 10"))product=product.Replace("Windows 10","Windows 11");
                data["Sistema operacional"]=product;
                data["Versão do sistema"]=$"{os?.GetValue("DisplayVersion")} (build {build}.{os?.GetValue("UBR")})";
            });
            Read(()=>{
                using var cpu=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                data["Processador"]=cpu?.GetValue("ProcessorNameString")?.ToString()?.Trim()??"Não informado";
                using var bios=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                foreach(var pair in new[]{("Fabricante","SystemManufacturer"),("Modelo","SystemProductName"),("Placa-mãe","BaseBoardProduct"),("Fabricante da placa-mãe","BaseBoardManufacturer"),("BIOS","BIOSVersion")})
                {
                    var value=bios?.GetValue(pair.Item2);
                    if(value is not null)data[pair.Item1]=value is string[] lines?string.Join(", ",lines):value.ToString()!;
                }
                using var role=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions");
                data["Tipo de sistema"]=role?.GetValue("ProductType")?.ToString() switch{"ServerNT"=>"Windows Server","LanmanNT"=>"Controlador de domínio",_=>"Estação de trabalho"};
            });
            Read(()=>{
                var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
                if(GlobalMemoryStatusEx(ref memory)){data["RAM total"]=Bytes(memory.TotalPhysical);data["RAM disponível"]=Bytes(memory.AvailablePhysical);}
            });
        }
        if(OperatingSystem.IsLinux())
        {
            Read(()=>{
                var os=File.ReadLines("/etc/os-release").FirstOrDefault(l=>l.StartsWith("PRETTY_NAME="));
                if(os is not null)data["Sistema operacional"]=os["PRETTY_NAME=".Length..].Trim('"');
                var lines=File.ReadAllLines("/proc/cpuinfo");
                data["Processador"]=lines.FirstOrDefault(l=>l.StartsWith("model name")||l.StartsWith("Model")||l.StartsWith("Hardware"))?.Split(':',2).Last().Trim()??RuntimeInformation.ProcessArchitecture.ToString();
                var model="/sys/firmware/devicetree/base/model";
                if(File.Exists(model))data["Modelo"]=File.ReadAllText(model).TrimEnd('\0','\n');
            });
            Read(()=>{
                foreach(var line in File.ReadLines("/proc/meminfo"))
                {
                    var parts=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                    if(parts.Length>=2&&ulong.TryParse(parts[1],out var kb))
                    {
                        if(parts[0]=="MemTotal:")data["RAM total"]=Bytes(kb*1024);
                        if(parts[0]=="MemAvailable:")data["RAM disponível"]=Bytes(kb*1024);
                    }
                }
            });
        }
        Read(()=>{
            var domain=IPGlobalProperties.GetIPGlobalProperties().DomainName;
            data["Sufixo DNS"]=string.IsNullOrWhiteSpace(domain)?"Não informado":domain;
            // A DNS suffix alone does not prove membership in an Active Directory domain.
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback))
            {
                var ips=nic.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Select(a=>a.Address.ToString());
                data[$"Rede: {nic.Name}"]=$"IPv4: {string.Join(", ",ips)} | MAC: {nic.GetPhysicalAddress()}";
            }
        });
        foreach(var drive in DriveInfo.GetDrives())
            Read(()=>{if(drive.IsReady&&drive.DriveType==DriveType.Fixed)data[$"Disco: {drive.Name}"]=$"{Bytes((ulong)drive.TotalSize)} total | {Bytes((ulong)drive.AvailableFreeSpace)} livre | {drive.DriveFormat}";});
        return data;
    }
    static string Bytes(ulong value)=>(value/1073741824d).ToString("N2",CultureInfo.GetCultureInfo("pt-BR"))+" GiB";
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length,Load;
        public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
