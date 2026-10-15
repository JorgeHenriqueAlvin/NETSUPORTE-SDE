using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SdeSuporte;

public record TeamCredential(TechnicianKey Technician,long Issued,long Expires,string Signature);

public static class TeamAccess
{
    // The installed client contains only this public authority and an agent-enrollment
    // delegation. Private authorization is packaged separately for the IT installer,
    // never in public source, public build artifacts, or the client installer.
    static readonly Lazy<DeploymentProfile> bundled=new(()=>{
        using var stream=typeof(TeamAccess).Assembly.GetManifestResourceStream("SdeSuporte.TeamDeployment")
            ??throw new InvalidDataException("Autorização pública da equipe ausente.");
        var profile=JsonSerializer.Deserialize<DeploymentProfile>(stream,Json.Options)??throw new InvalidDataException("Equipe inválida.");
        Enrollment.VerifyPermission(profile.Approval);
        if(!profile.Approval.Permission.TeamWide)throw new InvalidDataException("Equipe sem autorização compartilhada.");
        return profile;
    });
    public static DeploymentProfile Deployment=>bundled.Value;
    public static TechnicianKey Root=>Deployment.Approval.Permission.Issuer;
    static byte[] CredentialBytes(TeamCredential credential)=>Encoding.UTF8.GetBytes("SDE-TECHNICIAN-TEAM-1\n")
        .Concat(Enrollment.Bytes(credential with{Signature=""})).ToArray();
    public static string EncodeCode(byte[] scalar)=>"SDE1-"+Convert.ToBase64String(scalar).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static TechnicianIdentity OpenCode(string code,string name)
    {
        code=string.Concat(code.Where(c=>!char.IsWhiteSpace(c)));
        if(!code.StartsWith("SDE1-",StringComparison.OrdinalIgnoreCase)||code.Length!=48)throw new InvalidDataException("Código da equipe inválido. Copie o código completo fornecido pela TI.");
        byte[] scalar=Convert.FromBase64String(code[5..].Replace('-','+').Replace('_','/')+"=");
        try
        {
            if(scalar.Length!=32)throw new InvalidDataException("Código da equipe inválido.");
            // RFC 5915 ECPrivateKey: importing the private scalar derives the public point
            // in Windows CNG; no public-key or private-key file exchange is needed.
            var writer=new AsnWriter(AsnEncodingRules.DER);writer.PushSequence();writer.WriteInteger(1);writer.WriteOctetString(scalar);
            var tag=new Asn1Tag(TagClass.ContextSpecific,0,true);writer.PushSequence(tag);writer.WriteObjectIdentifier("1.2.840.10045.3.1.7");writer.PopSequence(tag);writer.PopSequence();
            var key=ECDsa.Create();
            try{key.ImportECPrivateKey(writer.Encode(),out _);return new TechnicianIdentity(key,name);}catch{key.Dispose();throw;}
        }finally{CryptographicOperations.ZeroMemory(scalar);}
    }
    public static TeamCredential Authorize(string code,TechnicianIdentity technician,TechnicianKey root)
    {
        using var issuer=OpenCode(code,root.Name);
        if(issuer.Public.Id!=root.Id)throw new InvalidDataException("Este código não pertence à equipe deste instalador.");
        return Issue(issuer,technician.Public);
    }
    public static TeamCredential Issue(TechnicianIdentity issuer,TechnicianKey technician)
    {
        Enrollment.VerifyPublic(technician);
        var credential=new TeamCredential(technician,DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds(),DateTimeOffset.UtcNow.AddYears(5).ToUnixTimeSeconds(),"");
        return credential with{Signature=issuer.SignDetached(CredentialBytes(credential))};
    }
    public static TechnicianKey? Verify(TeamCredential? credential,TechnicianKey root)
    {
        if(credential is null)return null;
        try
        {
            long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if(credential.Issued>now+45||credential.Expires<=now||credential.Expires-credential.Issued>TimeSpan.FromDays(1830).TotalSeconds)return null;
            Enrollment.VerifyPublic(root);Enrollment.VerifyPublic(credential.Technician);
            using var key=ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(root.PublicKey),out _);
            return key.VerifyData(CredentialBytes(credential),Convert.FromBase64String(credential.Signature),HashAlgorithmName.SHA256)?credential.Technician:null;
        }catch{return null;}
    }
    public static string Encode(TeamCredential credential)=>Convert.ToBase64String(Enrollment.Bytes(credential));
    public static TeamCredential? Decode(string encoded)
    {
        if(encoded.Length is 0 or >6000)return null;
        try{return JsonSerializer.Deserialize<TeamCredential>(Convert.FromBase64String(encoded),Json.Options);}catch{return null;}
    }
}
