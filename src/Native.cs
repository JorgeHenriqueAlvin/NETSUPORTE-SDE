using System.Runtime.InteropServices;
using System.Text;
namespace SdeSuporte;
static class Native
{
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSE mouse; [FieldOffset(0)] public KEY key; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int dx,dy; public uint mouseData,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEY { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder value,int length,out int needed);
    public static bool DesktopAvailable()
    {
        var desktop=OpenInputDesktop(0,false,1);
        if(desktop==IntPtr.Zero)return false;
        try {var name=new StringBuilder(256);return GetUserObjectInformation(desktop,2,name,512,out _)&&name.ToString()=="Default";}
        finally{CloseDesktop(desktop);}
    }
    static void Send(params INPUT[] values) {if(SendInput((uint)values.Length,values,Marshal.SizeOf<INPUT>())!=values.Length)throw new InvalidOperationException("O Windows bloqueou a entrada. Verifique se o programa remoto está elevado.");}
    static INPUT Key(ushort key,ushort scan,uint flags)=>new(){type=1,data=new(){key=new(){vk=key,scan=scan,flags=flags}}};
    static INPUT Mouse(uint flags,int delta=0)=>new(){data=new(){mouse=new(){flags=flags,mouseData=unchecked((uint)delta)}}};
    public static void Apply(InputCommand c)
    {
        if(c.Kind is "move" or "down" or "up")
        {
            var b=SystemInformation.VirtualScreen;
            if(c.X<0||c.Y<0||c.X>=b.Width||c.Y>=b.Height)throw new ArgumentOutOfRangeException();
            SetCursorPos(c.X+b.X,c.Y+b.Y);
            if(c.Kind!="move")Send(Mouse(c.Key==2?(c.Kind=="down"?8u:16u):(c.Kind=="down"?2u:4u)));
        }
        else if(c.Kind=="wheel")Send(Mouse(0x800,Math.Clamp(c.Delta,-1200,1200)));
        else if(c.Kind=="text") {foreach(char ch in c.Text)Send(Key(0,ch,4),Key(0,ch,6));}
        else if(c.Kind is "keyDown" or "keyUp")
        {
            if(c.Key<8||c.Key>254)throw new ArgumentOutOfRangeException();
            uint extended=c.Key is >=33 and <=46 or 91 or 92 or 111 or 144 or 163 or 165 ? 1u : 0u;
            Send(Key((ushort)c.Key,0,extended|(c.Kind=="keyUp"?2u:0u)));
        }
        else throw new ArgumentException("Comando inválido.");
    }
    public static void ReleaseInput()
    {
        foreach(ushort k in new ushort[]{16,17,18,91,92,160,161,162,163,164,165})try{Send(Key(k,0,2));}catch{}
        try{Send(Mouse(4),Mouse(16));}catch{}
    }
}
