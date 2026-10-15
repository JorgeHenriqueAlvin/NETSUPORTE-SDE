using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SdeSuporte;
public record EnrollmentPermission(string Id,TechnicianKey Issuer,TechnicianKey[] Technicians,string EnrollmentPublicKey,string Sector,DateTimeOffset Expires);
public record SignedPermission(EnrollmentPermission Permission,string Signature);
public record DeploymentProfile(SignedPermission Approval,string EnrollmentPrivateKey);
public record DeviceEnrollment(Device Device,SignedPermission Approval,string Signature);
public static class Enrollment
{
    public static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Json.Options);
    public static byte[] PermissionBytes(EnrollmentPermission p)=>Encoding.UTF8.GetBytes("SDE-ENROLLMENT-1\n").Concat(Bytes(p)).ToArray();
    static byte[] DeviceBytes(Device d,EnrollmentPermission p)=>Encoding.UTF8.GetBytes("SDE-DEVICE-1\n").Concat(Bytes(d)).Concat(SHA256.HashData(PermissionBytes(p))).ToArray();
    public static void VerifyPublic(TechnicianKey technician)
    {
        byte[] data=Convert.FromBase64String(technician.PublicKey);
        using var k=ECDsa.Create();k.ImportSubjectPublicKeyInfo(data,out int read);
        if(read!=data.Length||k.KeySize!=256||Convert.ToHexString(SHA256.HashData(data))!=technician.Id||string.IsNullOrWhiteSpace(technician.Name)||technician.Name.Length>80||technician.Name.Any(char.IsControl))
            throw new InvalidDataException("Identificação de técnico inválida.");
    }
    public static DeploymentProfile Create(TechnicianIdentity identity,IEnumerable<TechnicianKey> technicians,string sector)
    {
        var list=technicians.Append(identity.Public).DistinctBy(t=>t.Id).ToArray();
        foreach(var technician in list)VerifyPublic(technician);
        if(string.IsNullOrWhiteSpace(sector)||sector.Length>100)throw new ArgumentException("Informe o setor.");
        using var enrollment=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p=new EnrollmentPermission(Guid.NewGuid().ToString(),identity.Public,list,Convert.ToBase64String(enrollment.ExportSubjectPublicKeyInfo()),sector.Trim(),DateTimeOffset.UtcNow.AddYears(2));
        return new(new(p,identity.SignDetached(PermissionBytes(p))),Convert.ToBase64String(enrollment.ExportPkcs8PrivateKey()));
    }
    public static void VerifyPermission(SignedPermission approval)
    {
        var p=approval.Permission;VerifyPublic(p.Issuer);
        if(!Guid.TryParse(p.Id,out _)||p.Expires<=DateTimeOffset.UtcNow||p.Technicians.Length is <1 or >30||string.IsNullOrWhiteSpace(p.Sector)||p.Sector.Length>100)throw new InvalidDataException("Autorização de instalação inválida ou expirada.");
        foreach(var t in p.Technicians)VerifyPublic(t);
        if(p.Technicians.Select(t=>t.Id).Distinct().Count()!=p.Technicians.Length)throw new InvalidDataException("Técnicos duplicados na autorização.");
        using var issuer=ECDsa.Create();issuer.ImportSubjectPublicKeyInfo(Convert.FromBase64String(p.Issuer.PublicKey),out _);
        if(!issuer.VerifyData(PermissionBytes(p),Convert.FromBase64String(approval.Signature),HashAlgorithmName.SHA256))throw new InvalidDataException("A autorização de instalação foi alterada.");
        using var delegation=ECDsa.Create();delegation.ImportSubjectPublicKeyInfo(Convert.FromBase64String(p.EnrollmentPublicKey),out _);
        if(delegation.KeySize!=256)throw new InvalidDataException("Chave de instalação inválida.");
    }
    public static DeviceEnrollment Seal(Device device,DeploymentProfile profile)
    {
        VerifyPermission(profile.Approval);device.Validate();
        if(device.Sector!=profile.Approval.Permission.Sector)throw new InvalidDataException("Setor divergente da autorização.");
        using var key=ECDsa.Create();key.ImportPkcs8PrivateKey(Convert.FromBase64String(profile.EnrollmentPrivateKey),out _);
        if(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())!=profile.Approval.Permission.EnrollmentPublicKey)throw new InvalidDataException("Chave de instalação divergente.");
        return new(device,profile.Approval,Convert.ToBase64String(key.SignData(DeviceBytes(device,profile.Approval.Permission),HashAlgorithmName.SHA256)));
    }
    public static void VerifyDevice(DeviceEnrollment enrollment,IEnumerable<string> trustedIssuers,string technicianId)
    {
        VerifyPermission(enrollment.Approval);
        var p=enrollment.Approval.Permission;
        if(!trustedIssuers.Contains(p.Issuer.Id)||!p.Technicians.Any(t=>t.Id==technicianId)||p.Sector!=enrollment.Device.Sector)
            throw new InvalidDataException("Computador de equipe não autorizada.");
        enrollment.Device.Validate();
        using var key=ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(p.EnrollmentPublicKey),out _);
        if(!key.VerifyData(DeviceBytes(enrollment.Device,p),Convert.FromBase64String(enrollment.Signature),HashAlgorithmName.SHA256))throw new InvalidDataException("Identificação do computador foi alterada.");
    }
}
public static class InstallerPackage
{
    static readonly byte[] Magic=Encoding.ASCII.GetBytes("SDE-AUTO-SETUP-1");
    const int FooterLength=32;
    public static void CreateTemplate(string bootstrapper,string payloadZip,string destination)
    {
        using var output=File.Create(destination);using(var source=File.OpenRead(bootstrapper))source.CopyTo(output);
        long payloadLength;
        using(var zip=File.OpenRead(payloadZip)){payloadLength=zip.Length;zip.CopyTo(output);}
        using var writer=new BinaryWriter(output);writer.Write(payloadLength);writer.Write(0L);writer.Write(Magic);
    }
    public static void Personalize(string template,string destination,DeploymentProfile profile)
    {
        Enrollment.VerifyPermission(profile.Approval);
        using var input=File.OpenRead(template);var sizes=ReadFooter(input);
        if(sizes.ProfileLength!=0)throw new InvalidDataException("Modelo de instalador inválido.");
        using var output=File.Create(destination);input.Position=0;
        long remaining=input.Length-FooterLength;
        byte[] buffer=new byte[65536];while(remaining>0){int count=input.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(count==0)throw new EndOfStreamException();output.Write(buffer,0,count);remaining-=count;}
        byte[] metadata=Enrollment.Bytes(profile);output.Write(metadata);
        using var writer=new BinaryWriter(output);writer.Write(sizes.PayloadLength);writer.Write((long)metadata.Length);writer.Write(Magic);
    }
    static (long PayloadLength,long ProfileLength) ReadFooter(Stream source)
    {
        if(source.Length<FooterLength)throw new InvalidDataException("Instalador incompleto.");
        source.Position=source.Length-FooterLength;using var reader=new BinaryReader(source,Encoding.UTF8,true);
        long zip=reader.ReadInt64(),profile=reader.ReadInt64();byte[] magic=reader.ReadBytes(Magic.Length);
        if(!magic.SequenceEqual(Magic)||zip<1||profile<0||profile>200000||zip+profile>source.Length-FooterLength)throw new InvalidDataException("Instalador inválido.");
        return(zip,profile);
    }
    public static DeploymentProfile Extract(string package,string directory)
    {
        using var input=File.OpenRead(package);var sizes=ReadFooter(input);
        if(sizes.ProfileLength==0)throw new InvalidDataException("Gere o instalador pelo console do técnico.");
        input.Position=input.Length-FooterLength-sizes.ProfileLength;byte[] metadata=new byte[(int)sizes.ProfileLength];input.ReadExactly(metadata);
        var profile=JsonSerializer.Deserialize<DeploymentProfile>(metadata,Json.Options)??throw new InvalidDataException("Perfil inválido.");Enrollment.VerifyPermission(profile.Approval);
        input.Position=input.Length-FooterLength-sizes.ProfileLength-sizes.PayloadLength;
        string archive=Path.Combine(directory,"payload.zip");using(var output=File.Create(archive)){long remaining=sizes.PayloadLength;byte[] buffer=new byte[65536];while(remaining>0){int count=input.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(count==0)throw new EndOfStreamException();output.Write(buffer,0,count);remaining-=count;}}
        ZipFile.ExtractToDirectory(archive,directory,true);File.Delete(archive);Json.Write(Path.Combine(directory,"deployment.json"),profile);return profile;
    }
}
