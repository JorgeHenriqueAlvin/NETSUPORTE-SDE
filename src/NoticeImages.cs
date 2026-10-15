using System.Drawing.Imaging;
namespace SdeSuporte;

public static class NoticeImages
{
    public const int MaximumBytes=1500000;
    public static string Caption(string text,bool hasPhoto)=>string.IsNullOrWhiteSpace(text)&&hasPhoto?"":MessageHistory.Validate(text);
    public static Bitmap Decode(string encoded)
    {
        try
        {
            if(encoded.Length>2000000)throw new ArgumentException("Foto maior que o limite de 1,5 MB.");
            byte[] bytes=Convert.FromBase64String(encoded);
            if(bytes.Length>MaximumBytes)throw new ArgumentException("Foto muito grande.");
            using var stream=new MemoryStream(bytes);using var image=Image.FromStream(stream,true,true);
            if(image.Width>1600||image.Height>1600||image.Width<1||image.Height<1||
                (image.RawFormat.Guid!=ImageFormat.Jpeg.Guid&&image.RawFormat.Guid!=ImageFormat.Png.Guid))
                throw new ArgumentException("Use foto JPEG ou PNG com até 1.600 pixels por lado.");
            return new Bitmap(image);
        }
        catch(Exception e)when(e is FormatException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {throw new ArgumentException("Foto inválida ou danificada.",e);}
    }
    public static string FromFile(string path)
    {
        if(new FileInfo(path).Length>8*1024*1024)throw new ArgumentException("Escolha uma foto de até 8 MB.");
        using var source=Image.FromFile(path);
        if(source.Width>8000||source.Height>8000||(long)source.Width*source.Height>24000000||
            (source.RawFormat.Guid!=ImageFormat.Jpeg.Guid&&source.RawFormat.Guid!=ImageFormat.Png.Guid))throw new ArgumentException("Use uma foto JPEG ou PNG de até 24 megapixels.");
        double scale=Math.Min(1,1600.0/Math.Max(source.Width,source.Height));
        using var resized=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)));
        using(var canvas=Graphics.FromImage(resized)){canvas.Clear(Color.White);canvas.DrawImage(source,new Rectangle(Point.Empty,resized.Size));}
        using var output=new MemoryStream();using var options=new EncoderParameters(1);
        options.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,80L);
        resized.Save(output,ImageCodecInfo.GetImageEncoders().Single(c=>c.FormatID==ImageFormat.Jpeg.Guid),options);
        if(output.Length>MaximumBytes)throw new ArgumentException("A foto continua muito grande. Escolha uma imagem menor.");
        return Convert.ToBase64String(output.ToArray());
    }
}
