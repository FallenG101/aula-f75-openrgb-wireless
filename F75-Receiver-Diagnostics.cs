// Original diagnostic utility for this investigation. No lighting/configuration writes.
// Model query framing: OpenRGB issue 5166 / wireless draft MR 3026.
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;

class F75ReceiverDiagnostics
{
    const string Dll = @"C:\Program Files\OpenRGB\hidapi-hotplug.dll";
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
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr dev, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    static string Wide(IntPtr p) { return p==IntPtr.Zero ? "" : Marshal.PtrToStringUni(p); }
    static string Hex(byte[] b, int n) { return BitConverter.ToString(b,0,n).Replace('-', ' '); }
    static bool GetCaps(string path, out Caps caps) {
        caps=new Caps(); IntPtr h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(h==new IntPtr(-1)) return false;
        try { IntPtr data; if(!HidD_GetPreparsedData(h,out data)) return false;
            try { return HidP_GetCaps(data,out caps)==0x110000; }
            finally { HidD_FreePreparsedData(data); }
        } finally { CloseHandle(h); }
    }
    static string Collect() {
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75-diagnostics");
        Directory.CreateDirectory(directory);
        StringBuilder log=new StringBuilder();
        log.AppendLine("F75 Pro receiver investigation - "+DateTimeOffset.Now.ToString("o"));
        log.AppendLine("OS: "+Environment.OSVersion+"; x64 process: "+Environment.Is64BitProcess);
        log.AppendLine("Only target: 3554:FA09. Only command: nonpersistent model-info query 13 05 01 00 ... 19.");
        foreach(Process p in Process.GetProcesses()) {
            try { string name=p.ProcessName;
                if(System.Text.RegularExpressions.Regex.IsMatch(name,"OpenRGB|Aula|SignalRGB|RGBFusion|iCUE|Armoury|Lighting|Mystic|Keylux",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    log.AppendLine("Possible lighting process: "+name+" PID "+p.Id);
            } catch {} finally { p.Dispose(); }
        }
        if(!File.Exists(Dll)) throw new FileNotFoundException("Installed OpenRGB HID library missing",Dll);
        IntPtr head=hid_enumerate(0x3554,0xFA09); int count=0, targets=0;
        try {
            List<string> candidates=new List<string>();
            for(IntPtr ptr=head; ptr!=IntPtr.Zero;) {
                Info i=(Info)Marshal.PtrToStructure(ptr,typeof(Info)); ptr=i.next; count++;
                string path=Marshal.PtrToStringAnsi(i.path);
                log.AppendLine(); log.AppendLine("PATH: "+path);
                log.AppendFormat("VID:PID {0:X4}:{1:X4}; release {2:X4}; interface {3}; page {4:X4}; usage {5:X4}\r\n",i.vid,i.pid,i.release,i.iface,i.page,i.usage);
                log.AppendLine("Manufacturer: "+Wide(i.manufacturer)+"; product: "+Wide(i.product));
                Caps caps; bool valid=GetCaps(path,out caps);
                if(valid) log.AppendFormat("Report lengths (include ID): input={0}, output={1}, feature={2}\r\n",caps.input,caps.output,caps.feature);
                else log.AppendLine("Could not read report capabilities.");
                IntPtr dev=hid_open_path(path);
                if(dev==IntPtr.Zero) { log.AppendLine("HID open failed: "+Wide(hid_error(IntPtr.Zero))); continue; }
                try {
                    byte[] desc=new byte[4096];
                    try { int n=hid_get_report_descriptor(dev,desc,(UIntPtr)desc.Length);
                        log.AppendLine("Report descriptor bytes: "+n);
                        if(n>0 && n<=desc.Length) {
                            byte[] exact=new byte[n]; Array.Copy(desc,exact,n);
                            File.WriteAllBytes(Path.Combine(directory,"collection-"+count+"-descriptor.bin"),exact);
                            log.AppendLine(Hex(desc,n));
                        }
                    } catch(EntryPointNotFoundException) { log.AppendLine("Installed HID library has no descriptor export; capability lengths recorded above."); }
                } finally { hid_close(dev); }
                if(valid && i.iface==1 && i.page==0xFF02 && i.usage==2 && caps.output==20 && caps.input==20)
                    candidates.Add(path);
            }
            targets=candidates.Count;
            if(targets==1) {
                IntPtr dev=hid_open_path(candidates[0]);
                if(dev==IntPtr.Zero) log.AppendLine("Model query: could not open uniquely matched collection.");
                else try {
                    byte[] query=new byte[20]; query[0]=0x13; query[1]=5; query[2]=1; query[19]=0x19;
                    int sent=hid_write(dev,query,(UIntPtr)query.Length);
                    log.AppendLine("Model query TX: "+Hex(query,20)+"; write result="+sent);
                    if(sent==20) {
                        for(int attempt=0;attempt<4;attempt++) {
                            byte[] response=new byte[64]; int n=hid_read_timeout(dev,response,(UIntPtr)response.Length,500);
                            log.AppendLine("Model query RX ("+n+"): "+(n>0 ? Hex(response,n) : Wide(hid_error(dev))));
                            if(n==20 && response[0]==0x13 && response[1]==5 && response[2]==1 && response[3]==0) {
                                int sum=0; for(int x=0;x<19;x++) sum+=response[x];
                                bool checksum=(byte)sum==response[19];
                                log.AppendLine("Model reply checksum valid: "+checksum);
                                if(checksum) log.AppendLine("MODEL ID: 0x"+response[10].ToString("X2"));
                                break;
                            }
                        }
                    }
                } finally { hid_close(dev); }
            } else log.AppendLine("Model query skipped: expected one exact RGB collection, found "+targets+".");
        } finally { hid_free_enumeration(head); }
        log.AppendLine("\r\nReceiver collections: "+count+"; exact query targets: "+targets);
        string filename=Path.Combine(directory,"receiver-diagnostics.txt");
        File.WriteAllText(filename,log.ToString(),Encoding.UTF8);
        return "Saved receiver-diagnostics.txt in:\r\n"+directory+"\r\n\r\n"+log;
    }
    [STAThread] static void Main(string[] args) {
        if(args.Length==1 && args[0]=="--collect") { try { Console.WriteLine(Collect()); Environment.ExitCode=0; } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } return; }
        Application.EnableVisualStyles(); Form f=new Form {Text="F75 Pro - read-only receiver diagnostics",Width=900,Height=650};
        TextBox text=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,WordWrap=false,Font=new System.Drawing.Font("Consolas",10)};
        text.Text="Reading receiver interfaces and requesting model information.\r\nNo lighting, remapping, flash, reset, or firmware commands. Please wait...";
        f.Controls.Add(text);
        f.Shown+=async delegate { try { text.Text=await Task.Run((Func<string>)Collect); } catch(Exception ex) { text.Text="Diagnostic failed:\r\n"+ex; } };
        Application.Run(f);
    }
}
