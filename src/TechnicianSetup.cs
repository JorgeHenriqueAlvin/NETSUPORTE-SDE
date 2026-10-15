using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SdeSuporte;

public record TechnicianSetupGrant(string TeamId,string TeamCode,DateTimeOffset Expires,string InstallerSha256);

public static class TechnicianSetup
{
    static readonly byte[] Magic=Encoding.ASCII.GetBytes("SDE-TECH-SETUP-1");
    const int FooterLength=24;
    static string Hash(Stream input,long length)
    {
        input.Position=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[65536];
        while(length>0){int count=input.Read(buffer,0,(int)Math.Min(length,buffer.Length));if(count==0)throw new EndOfStreamException();hash.AppendData(buffer,0,count);length-=count;}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static void Validate(TechnicianSetupGrant grant,TechnicianKey expectedRoot)
    {
        if(grant.TeamId!=expectedRoot.Id||grant.Expires<=DateTimeOffset.UtcNow)throw new InvalidDataException("Instalador técnico sem autorização válida para esta equipe.");
        using var issuer=TeamAccess.OpenCode(grant.TeamCode,expectedRoot.Name);
        if(issuer.Public.Id!=expectedRoot.Id)throw new InvalidDataException("A autorização do instalador técnico foi alterada.");
    }
    public static void Create(string source,string destination,string code,TechnicianKey root)
    {
        if(Path.GetFullPath(source)==Path.GetFullPath(destination))throw new ArgumentException("Escolha outro arquivo para o instalador autorizado.");
        using var input=File.OpenRead(source);
        var grant=new TechnicianSetupGrant(root.Id,code,DateTimeOffset.UtcNow.AddYears(5),Hash(input,input.Length));Validate(grant,root);
        using var output=File.Create(destination);input.Position=0;input.CopyTo(output);
        byte[] metadata=JsonSerializer.SerializeToUtf8Bytes(grant,Json.Options);output.Write(metadata);
        using var footer=new BinaryWriter(output);footer.Write((long)metadata.Length);footer.Write(Magic);
    }
    public static TechnicianSetupGrant? Read(string file,TechnicianKey root)
    {
        using var input=File.OpenRead(file);if(input.Length<FooterLength)return null;
        input.Position=input.Length-Magic.Length;byte[] magic=new byte[Magic.Length];input.ReadExactly(magic);
        if(!magic.SequenceEqual(Magic))return null;
        input.Position=input.Length-FooterLength;using var reader=new BinaryReader(input,Encoding.UTF8,true);
        long size=reader.ReadInt64(),baseLength=input.Length-FooterLength-size;
        if(size is <1 or >8192||baseLength<64)throw new InvalidDataException("Instalador técnico incompleto.");
        input.Position=baseLength;byte[] metadata=new byte[(int)size];input.ReadExactly(metadata);
        var grant=JsonSerializer.Deserialize<TechnicianSetupGrant>(metadata,Json.Options)??throw new InvalidDataException("Autorização do instalador ausente.");
        if(!string.Equals(Hash(input,baseLength),grant.InstallerSha256,StringComparison.Ordinal))throw new InvalidDataException("O instalador técnico foi alterado ou está incompleto.");
        Validate(grant,root);return grant;
    }
}

public static class TechnicianProfiles
{
    public static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte");
    public static TechnicianIdentity Load(string directory,Func<string> getName)
    {
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,"identity.json");LocalIdentity metadata;
        if(File.Exists(path))metadata=Json.Read<LocalIdentity>(path);
        else
        {
            string name=getName();
            if(string.IsNullOrWhiteSpace(name)||name.Length>80||name.Any(char.IsControl))throw new ArgumentException("Nome do técnico inválido.");
            metadata=new(name.Trim(),"SDE-Suporte-"+Guid.NewGuid().ToString("N"));
            using var key=CngKey.Create(CngAlgorithm.ECDsaP256,metadata.KeyName,new CngKeyCreationParameters{ExportPolicy=CngExportPolicies.None});
            Json.Write(path,metadata);
        }
        return new(new ECDsaCng(CngKey.Open(metadata.KeyName)),metadata.Name);
    }
    public static void Authorize(string directory,TechnicianSetupGrant grant,TechnicianKey root)
    {
        TechnicianSetup.Validate(grant,root);
        // Runs as the original, non-elevated Windows user, never the UAC administrator.
        // Only a public credential and a reference to the non-exportable user key remain.
        using var identity=Load(directory,()=>Environment.UserName);
        var credential=TeamAccess.Authorize(grant.TeamCode,identity,root);
        Json.Write(Path.Combine(directory,"team-credential.json"),credential);
    }
    record LocalIdentity(string Name,string KeyName);
}
