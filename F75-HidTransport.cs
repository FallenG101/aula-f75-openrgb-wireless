// SPDX-License-Identifier: GPL-2.0-or-later
// Original transport implementation. Protocol references are in F75-Wireless-README.md.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

sealed class F75HidTransport : IDisposable
{
    const string Dll = @"C:\Program Files\OpenRGB\hidapi-hotplug.dll";
    IntPtr device;
    public string Path { get; private set; }
    [StructLayout(LayoutKind.Sequential)] struct Info {
        public IntPtr path; public ushort vid, pid; public IntPtr serial;
        public ushort release; public IntPtr manufacturer, product;
        public ushort page, usage; public int iface; public IntPtr next;
    }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort usage, page, input, output, feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] reserved;
        public ushort links, ib, iv, ii, ob, ov, oi, fb, fv, fi;
    }
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr hid_enumerate(ushort vid, ushort pid);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern void hid_free_enumeration(IntPtr list);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr hid_open_path([MarshalAs(UnmanagedType.LPStr)] string path);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern void hid_close(IntPtr dev);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int hid_write(IntPtr dev, byte[] data, UIntPtr length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int hid_read_timeout(IntPtr dev, byte[] data, UIntPtr length, int ms);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int hid_get_report_descriptor(IntPtr dev, byte[] data, UIntPtr length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr hid_error(IntPtr dev);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr dev, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    static bool MatchesCaps(string path) {
        IntPtr h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(h==new IntPtr(-1)) return false;
        try { IntPtr data; if(!HidD_GetPreparsedData(h,out data)) return false;
            try { Caps c; return HidP_GetCaps(data,out c)==0x110000 && c.page==0xFF02 && c.usage==2 && c.input==20 && c.output==20 && c.feature==0; }
            finally { HidD_FreePreparsedData(data); }
        } finally { CloseHandle(h); }
    }
    // Pin this proof of concept to the exact descriptor and revision observed on the owner's receiver.
    static readonly byte[] ExpectedDescriptor = {6,2,255,9,2,161,1,133,19,9,2,21,0,38,255,0,117,8,149,19,129,0,9,2,21,0,38,255,0,117,8,149,19,145,0,192};
    public static F75HidTransport Open() {
        IntPtr head=hid_enumerate(0x3554,0xFA09); List<string> paths=new List<string>();
        try { for(IntPtr p=head;p!=IntPtr.Zero;) {
            Info i=(Info)Marshal.PtrToStructure(p,typeof(Info)); p=i.next;
            if(i.release!=0x3000 || i.iface!=1 || i.page!=0xFF02 || i.usage!=2) continue;
            string path=Marshal.PtrToStringAnsi(i.path); if(MatchesCaps(path)) paths.Add(path);
        }} finally { hid_free_enumeration(head); }
        if(paths.Count!=1) throw new InvalidOperationException("Expected one matching receiver RGB collection; found "+paths.Count+". Keep its receiver plugged in, cable unplugged, and keyboard awake in 2.4 GHz mode.");
        IntPtr dev=hid_open_path(paths[0]); if(dev==IntPtr.Zero) throw new InvalidOperationException("Could not open receiver RGB interface.");
        try {
            byte[] desc=new byte[256]; int n=hid_get_report_descriptor(dev,desc,(UIntPtr)desc.Length);
            if(n!=ExpectedDescriptor.Length) throw new InvalidOperationException("Receiver descriptor differs from the verified unit.");
            for(int k=0;k<n;k++) if(desc[k]!=ExpectedDescriptor[k]) throw new InvalidOperationException("Receiver descriptor differs from the verified unit.");
            byte[] query=new byte[20]; query[0]=0x13; query[1]=5; query[2]=1; query[19]=0x19;
            if(hid_write(dev,query,(UIntPtr)20)!=20) throw new InvalidOperationException("Receiver model query could not be sent.");
            bool matched=false;
            for(int attempt=0;attempt<4;attempt++) {
                byte[] reply=new byte[64]; n=hid_read_timeout(dev,reply,(UIntPtr)reply.Length,250);
                if(n!=20 || reply[0]!=0x13 || reply[1]!=5 || reply[2]!=1 || reply[3]!=0) continue;
                if(!F75Wire.ChecksumValid(reply)) continue;
                if(reply[10]!=0xCD) throw new InvalidOperationException("Unrecognized keyboard model 0x"+reply[10].ToString("X2")+". No lighting commands sent.");
                matched=true; break;
            }
            if(!matched) throw new InvalidOperationException("No valid model reply. Wake the keyboard and close other apps controlling this receiver.");
            F75HidTransport result=new F75HidTransport(); result.device=dev; result.Path=paths[0]; dev=IntPtr.Zero; return result;
        } finally { if(dev!=IntPtr.Zero) hid_close(dev); }
    }
    public void Write(byte[] frame) {
        // No arbitrary HID writes: only bounded, checksummed transient lighting packets.
        if(frame.Length!=20 || frame[0]!=0x13 || frame[1]!=0x88 || frame[2]<1 || frame[2]>29 || frame[3]>=frame[2] || frame[4]<0x11 || frame[4]>0x1E || !F75Wire.ChecksumValid(frame))
            throw new InvalidOperationException("Unsafe lighting frame rejected.");
        if(device==IntPtr.Zero || hid_write(device,frame,(UIntPtr)20)!=20) {
            IntPtr error=hid_error(device); throw new InvalidOperationException("Receiver write failed: "+(error==IntPtr.Zero ? "disconnected" : Marshal.PtrToStringUni(error)));
        }
    }
    public void Dispose() { if(device!=IntPtr.Zero) { hid_close(device); device=IntPtr.Zero; } }
}

static class F75Wire
{
    public static byte Sum(byte[] frame) { int sum=0; for(int k=0;k<19;k++) sum+=frame[k]; return (byte)sum; }
    public static bool ChecksumValid(byte[] frame) { return frame.Length>=20 && Sum(frame)==frame[19]; }
    public static List<byte[]> Frame(byte[] payload) {
        if(payload.Length==0 || payload.Length>400) throw new ArgumentException("Lighting messages must contain 1..400 bytes (at most 80 distinct key colors).");
        List<byte[]> frames=new List<byte[]>(); int chunks=(payload.Length+13)/14;
        for(int offset=0,index=0;offset<payload.Length;offset+=14,index++) {
            int count=Math.Min(14,payload.Length-offset); byte[] b=new byte[20];
            b[0]=0x13; b[1]=0x88; b[2]=(byte)chunks; b[3]=(byte)index; b[4]=(byte)(0x10+count);
            Array.Copy(payload,offset,b,5,count); b[19]=Sum(b); frames.Add(b);
        } return frames;
    }
    public static List<byte[]> Messages(uint[] colors, uint[] previous, byte[] hardware) {
        if(colors.Length!=hardware.Length || (previous!=null && previous.Length!=colors.Length)) throw new ArgumentException("Mismatched LED counts.");
        // Each message is a complete display frame. Splitting colors into separate messages
        // makes this F75 Pro alternate between partial frames, visibly blanking other keys.
        Dictionary<uint,List<byte>> groups=new Dictionary<uint,List<byte>>();
        for(int i=0;i<colors.Length;i++) {
            if(previous!=null && colors[i]==previous[i]) continue;
            uint color=colors[i]&0xFFFFFF; List<byte> keys;
            if(!groups.TryGetValue(color,out keys)) { keys=new List<byte>(); groups.Add(color,keys); } keys.Add(hardware[i]);
        }
        List<byte[]> result=new List<byte[]>(); List<byte> payload=new List<byte>();
        foreach(KeyValuePair<uint,List<byte>> group in groups) {
            payload.Add((byte)group.Key); payload.Add((byte)(group.Key>>8)); payload.Add((byte)(group.Key>>16)); payload.Add((byte)group.Value.Count);
            payload.AddRange(group.Value);
        }
        if(payload.Count>400) throw new ArgumentException("More than 80 colors/keys in a lighting frame.");
        if(payload.Count>0) result.Add(payload.ToArray()); return result;
    }
}
