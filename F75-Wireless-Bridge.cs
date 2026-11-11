// SPDX-License-Identifier: GPL-2.0-or-later
// Original standalone OpenRGB SDK adapter; see README for protocol attribution and limits.
using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;

static class F75Pacing {
    internal const double ReportGapMs=15,FrameQuietMs=15,MinimumFrameMs=100;
    internal static double FrameInterval(int reports){if(reports<1||reports>29)throw new ArgumentOutOfRangeException("reports");return Math.Max(MinimumFrameMs,reports*1000.0/60.0);}
}

// One-shot waits for the lighting thread; no spinning or global timer-resolution change.
// https://learn.microsoft.com/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw
sealed class F75Delay:IDisposable {
    [DllImport("kernel32.dll",EntryPoint="CreateWaitableTimerExW",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern IntPtr CreateWaitableTimerEx(IntPtr security,string name,uint flags,uint access);
    [DllImport("kernel32.dll")]static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr arg,bool resume);
    [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    IntPtr timer;
    internal bool HighResolution {get{return timer!=IntPtr.Zero;}}
    internal F75Delay(){try{timer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x00100002);}catch(EntryPointNotFoundException){}}
    internal void Wait(double milliseconds){
        if(milliseconds<=0)return;
        long due=-Math.Max(1L,(long)Math.Ceiling(milliseconds*10000));
        if(timer==IntPtr.Zero || !SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false) || WaitForSingleObject(timer,1000)!=0)Thread.Sleep((int)Math.Ceiling(milliseconds));
    }
    public void Dispose(){if(timer!=IntPtr.Zero){CloseHandle(timer);timer=IntPtr.Zero;}}
}

sealed class F75WirelessBridge : IDisposable
{
    internal const int Port=6743;
    internal static readonly string[][] Names={
        new[]{"Escape","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"},
        new[]{"Backtick","1","2","3","4","5","6","7","8","9","0","Minus","Equals","Backspace","Delete"},
        new[]{"Tab","Q","W","E","R","T","Y","U","I","O","P","Left Bracket","Right Bracket","Backslash","Page Up"},
        new[]{"Caps Lock","A","S","D","F","G","H","J","K","L","Semicolon","Quote","Enter","Page Down"},
        new[]{"Left Shift","Z","X","C","V","B","N","M","Comma","Period","Slash","Right Shift","Up Arrow","End"},
        new[]{"Left Control","Left Windows","Left Alt","Space","Fn","Right Control","Left Arrow","Down Arrow","Right Arrow"}
    };
    internal static readonly byte[][] HardwareRows={
        new byte[]{0,12,18,24,30,36,42,48,54,60,66,72,78},
        new byte[]{1,7,13,19,25,31,37,43,49,55,61,67,73,79,85},
        new byte[]{2,8,14,20,26,32,38,44,50,56,62,68,74,80,86},
        new byte[]{3,9,15,21,27,33,39,45,51,57,63,69,81,87},
        new byte[]{4,10,16,22,28,34,40,46,52,58,64,70,82,88},
        new byte[]{5,11,17,35,53,59,77,83,89}
    };
    internal static readonly byte[] Hardware=FlattenHardware();
    internal static readonly string[] LedNames=FlattenNames();
    readonly object gate=new object(), logGate=new object(), clientsGate=new object();
    readonly List<Client> clients=new List<Client>();
    readonly bool mock;
    readonly Action<double> observeMockReport;
    volatile bool running=true, available;
    uint[] colors=new uint[80]; bool enabled, paused; int generation;
    volatile bool smooth;
    internal bool Smooth { get { return smooth; } set { smooth=value; } }
    internal void Pause(bool value) { lock(gate) { paused=value; generation++; } Log(value?"Lighting paused; no lighting writes":"Lighting resumed"); }
    internal bool Paused { get { lock(gate) return paused; } }
    internal long Reports { get { lock(gate) return sentFrames; } }
    internal long Updates { get { lock(gate) return updates; } }
    long sentFrames, updates; string status="Starting"; TcpListener listener;
    Thread writer, acceptor; string logPath;
    sealed class Client { public TcpClient Socket; public object Gate=new object(); public uint Version; }
    static byte[] FlattenHardware() { List<byte> v=new List<byte>(); foreach(byte[] row in HardwareRows) v.AddRange(row); return v.ToArray(); }
    static string[] FlattenNames() { List<string> v=new List<string>(); foreach(string[] row in Names) v.AddRange(row); return v.ToArray(); }
    internal int ListeningPort { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    internal F75WirelessBridge(bool test,int port=Port,Action<double> reportObserver=null) {
        observeMockReport=reportObserver;
        mock=test; logPath=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,test ? "F75-bridge-mock.log" : "F75-bridge.log");
        listener=new TcpListener(IPAddress.Loopback,port); listener.Start();
        Log("Started on 127.0.0.1:"+Port+"; "+(mock?"MOCK: no HID access":"only 3554:FA09 revision 3000, descriptor-pinned, model CD"));
        Log("v0.2.3 candidate: report starts >=15ms apart; >=15ms quiet after frames; <=60 reports/s sustained, <=10 frames/s. Input lockup investigation; cause unconfirmed.");
        writer=new Thread(WriteLoop){IsBackground=true}; writer.Start();
        acceptor=new Thread(AcceptLoop){IsBackground=true}; acceptor.Start();
    }
    void Log(string text) { lock(logGate) { try { if(File.Exists(logPath)&&new FileInfo(logPath).Length>2097152){File.Copy(logPath,logPath+".previous",true);File.WriteAllText(logPath,"");}File.AppendAllText(logPath,DateTimeOffset.Now.ToString("o")+" "+text+Environment.NewLine); } catch {} } }
    void SetStatus(string text) { lock(gate) status=text; }
    internal string Status { get { lock(gate) return status+"\r\nLighting: "+(enabled?"active":"released")+" | RGB updates: "+updates+" | HID reports sent: "+sentFrames; } }
    internal void SetColors(uint[] frame,string reason) {
        if(frame.Length!=80) throw new ArgumentException("80 colors required");
        lock(gate) { colors=(uint[])frame.Clone(); enabled=true; generation++; updates++; }
        Log("Lighting update: "+reason);
    }
    internal void Solid(uint color) { uint[] frame=new uint[80]; for(int i=0;i<80;i++) frame[i]=color; SetColors(frame,"solid #"+((byte)color).ToString("X2")+((byte)(color>>8)).ToString("X2")+((byte)(color>>16)).ToString("X2")); }
    internal void KeyTest() {
        uint[] c=new uint[80]; c[Array.IndexOf(Hardware,(byte)0)]=0x0000FF; c[Array.IndexOf(Hardware,(byte)14)]=0x00FF00;
        c[Array.IndexOf(Hardware,(byte)35)]=0xFF0000; c[Array.IndexOf(Hardware,(byte)89)]=0x00FFFF;
        SetColors(c,"mapping test: Escape red, W green, Space blue, Right Arrow yellow; other keys off");
    }
    internal void Release() { lock(gate) { enabled=false; generation++; } Log("Lighting released; no reset/save command sent."); }
    void SetAvailable(bool value) { if(available==value) return; available=value; BroadcastChanged(); }
    void WriteLoop() {
        F75HidTransport transport=null; Stopwatch clock=Stopwatch.StartNew(); double nextFrameAt=0,lastReportAt=double.NegativeInfinity,lastHealthAt=0,maxWriteMs=0;
        using(F75Delay delay=new F75Delay())try { Log("Precise one-shot waits="+delay.HighResolution);while(running) {
            if(clock.Elapsed.TotalMilliseconds-lastHealthAt>=30000){lastHealthAt=clock.Elapsed.TotalMilliseconds;Log("Health: reports="+Reports+"; updates="+Updates+"; paused="+Paused+"; max_write_ms="+maxWriteMs.ToString("F1",System.Globalization.CultureInfo.InvariantCulture));maxWriteMs=0;}
            if(!mock && transport==null) {
                try { transport=F75HidTransport.Open(); nextFrameAt=0; SetStatus("Receiver identified: model CD. OpenRGB connection: 127.0.0.1:"+Port); SetAvailable(true); Log("Validated receiver: "+transport.Path); }
                catch(Exception ex) { SetAvailable(false); SetStatus(ex.Message); Log("Waiting for receiver: "+ex.Message); for(int k=0;k<20 && running;k++) Thread.Sleep(100); continue; }
            }
            if(mock) { SetAvailable(true); SetStatus("MOCK server - no keyboard access"); }
            bool active;lock(gate) active=enabled && !paused;
            if(!active) { Thread.Sleep(25); continue; }
            if(clock.Elapsed.TotalMilliseconds<nextFrameAt){delay.Wait(Math.Min(25,nextFrameAt-clock.Elapsed.TotalMilliseconds));continue;}
            uint[] snapshot;lock(gate)snapshot=(uint[])colors.Clone();
            bool renderSmooth=Smooth;
            try {
                // On this unit, an Escape-only refresh leaves the other keys dark. Refresh the
                // complete keyboard frame, including unchanged keys. The firmware cause is unproven.
                List<byte[]> packets=F75Render.Plan(snapshot,renderSmooth);
                // A frame-average cap alone permits short bursts that may disrupt typing.
                // Enforce both per-report spacing and a quiet interval after every frame.
                double interval=F75Pacing.FrameInterval(packets.Count);
                nextFrameAt=clock.Elapsed.TotalMilliseconds+interval;
                    foreach(byte[] packet in packets) {
                        while(running && clock.Elapsed.TotalMilliseconds-lastReportAt<F75Pacing.ReportGapMs){if(Paused)break;delay.Wait(F75Pacing.ReportGapMs-(clock.Elapsed.TotalMilliseconds-lastReportAt));}
                        lock(gate) {
                            if(!running || !enabled || paused) break;
                            lastReportAt=clock.Elapsed.TotalMilliseconds;
                            if(!mock) transport.Write(packet);else if(observeMockReport!=null)observeMockReport(lastReportAt);
                            maxWriteMs=Math.Max(maxWriteMs,clock.Elapsed.TotalMilliseconds-lastReportAt);sentFrames++;
                        }
                    }
                nextFrameAt=Math.Max(nextFrameAt,clock.Elapsed.TotalMilliseconds+F75Pacing.FrameQuietMs);
            } catch(Exception ex) {
                Log("HID failure: "+ex.Message); SetStatus(ex.Message); SetAvailable(false);
                if(transport!=null) transport.Dispose(); transport=null;
            }
        }} finally { SetAvailable(false); if(transport!=null) transport.Dispose(); }
    }
    internal static List<byte[]> Refresh(uint[] snapshot,bool changed,double elapsedMs) {
        return changed || elapsedMs>=80 ? F75Wire.Messages(snapshot,null,Hardware) : new List<byte[]>();
    }
    void AcceptLoop() { while(running) { try {
        TcpClient socket=listener.AcceptTcpClient(); socket.NoDelay=true; socket.SendTimeout=2000;
        Client c=new Client{Socket=socket}; lock(clientsGate) clients.Add(c);
        new Thread(delegate(){ClientLoop(c);}){IsBackground=true}.Start(); Log("OpenRGB client connected.");
    } catch(Exception ex) { if(running) Log("Accept failed: "+ex.Message); } } }
    static byte[] Exact(Stream stream,int length) {
        byte[] b=new byte[length]; int offset=0;
        while(offset<length) { int n=stream.Read(b,offset,length-offset); if(n==0) throw new EndOfStreamException(); offset+=n; } return b;
    }
    static void Send(Client c,uint id,byte[] payload) {
        lock(c.Gate) { BinaryWriter w=new BinaryWriter(c.Socket.GetStream(),Encoding.UTF8,true); w.Write(new byte[]{79,82,71,66}); w.Write((uint)0); w.Write(id); w.Write((uint)payload.Length); w.Write(payload); w.Flush(); }
    }
    void BroadcastChanged() { Client[] copy; lock(clientsGate) copy=clients.ToArray(); foreach(Client c in copy) { try { Send(c,100,new byte[0]); } catch { try { c.Socket.Close(); } catch {} } } }
    void ClientLoop(Client c) {
        try { Stream stream=c.Socket.GetStream(); while(running) {
            byte[] h=Exact(stream,16); if(h[0]!=79 || h[1]!=82 || h[2]!=71 || h[3]!=66) throw new InvalidDataException("Invalid SDK magic");
            uint dev=BitConverter.ToUInt32(h,4), id=BitConverter.ToUInt32(h,8), size=BitConverter.ToUInt32(h,12);
            if(size>65536) throw new InvalidDataException("SDK message too large"); byte[] data=Exact(stream,(int)size);
            if(id==40) { if(data.Length!=4) throw new InvalidDataException("Bad version packet"); c.Version=Math.Min(3,BitConverter.ToUInt32(data,0)); Send(c,40,BitConverter.GetBytes((uint)3)); }
            else if(id==50) { /* Client display name; no response required. */ }
            else if(id==0) Send(c,0,BitConverter.GetBytes((uint)(available?1:0)));
            else if(id==1 && dev==0 && available) {
                uint requested=data.Length==4 ? BitConverter.ToUInt32(data,0) : c.Version;
                uint[] snapshot; lock(gate) snapshot=(uint[])colors.Clone();
                Send(c,1,Description(Math.Min(c.Version,requested),mock,snapshot));
            }
            else if(dev==0 && available && (id==1050 || id==1051 || id==1052)) {
                lock(gate) { uint[] next; if(TryColors(id,data,colors,out next)) { colors=next; enabled=true; generation++; updates++; } else Log("Rejected malformed RGB packet "+id); }
            }
            else if(dev==0 && available && id==1100) { /* Direct is the only supported mode. No HID initialization required. */ }
            else if(dev==0 && available && id==1101) { /* Ignore redundant Direct-mode metadata; RGB packets carry actual colors. */ }
            else { Log("Ignored unsupported SDK packet "+id+" device "+dev); }
        }} catch(Exception ex) { if(running) Log("Client disconnected: "+ex.Message); }
        finally { lock(clientsGate) clients.Remove(c); c.Socket.Close(); }
    }
    internal static bool TryColors(uint id,byte[] data,uint[] current,out uint[] next) {
        next=null; if(current.Length!=80) return false; uint[] copy=(uint[])current.Clone();
        if(id==1052) { if(data.Length!=8) return false; uint index=BitConverter.ToUInt32(data,0); if(index>=80) return false; copy[index]=BitConverter.ToUInt32(data,4)&0xFFFFFF; }
        else {
            int offset=id==1050?4:id==1051?8:-1; if(offset<0 || data.Length<offset+2 || BitConverter.ToUInt32(data,0)!=data.Length) return false;
            if(id==1051 && BitConverter.ToUInt32(data,4)!=0) return false;
            int count=BitConverter.ToUInt16(data,offset); offset+=2; if(count>80 || data.Length!=offset+4*count) return false;
            for(int k=0;k<count;k++) copy[k]=BitConverter.ToUInt32(data,offset+k*4)&0xFFFFFF;
        } next=copy; return true;
    }
    static void Str(BinaryWriter w,string text) { byte[] b=Encoding.UTF8.GetBytes(text+"\0"); w.Write((ushort)b.Length); w.Write(b); }
    internal static byte[] Description(uint version,bool mock,uint[] snapshot=null) {
        using(MemoryStream m=new MemoryStream()) { BinaryWriter w=new BinaryWriter(m);
            w.Write((uint)0); w.Write((uint)5); Str(w,mock?"TEST F75 - no hardware":"AULA F75 Pro (2.4 GHz)");
            if(version>=1) Str(w,"AULA"); Str(w,"Transient wireless RGB via local SDK bridge"); Str(w,"bridge 0.2 / model CD"); Str(w,""); Str(w,"127.0.0.1:6743 / 3554:FA09 / MI_01 Col01");
            w.Write((ushort)1); w.Write(0); Str(w,"Direct"); w.Write((uint)0); w.Write((uint)32); w.Write((uint)0); w.Write((uint)0);
            if(version>=3) { w.Write((uint)0); w.Write((uint)0); }
            w.Write((uint)0); w.Write((uint)0); w.Write((uint)0); if(version>=3) w.Write((uint)0);
            w.Write((uint)0); w.Write((uint)1); w.Write((ushort)0);
            w.Write((ushort)1); Str(w,"Keyboard"); w.Write((uint)2); w.Write((uint)80); w.Write((uint)80); w.Write((uint)80);
            w.Write((ushort)(8+6*15*4)); w.Write((uint)6); w.Write((uint)15);
            // Matrix indexes are compact SDK LED indexes, independent from sparse hardware indexes.
            int led=0;
            for(int row=0;row<6;row++) for(int col=0;col<15;col++) {
                int local=-1;
                if(row==0) { if(col==0) local=0; else if(col>=2 && col<=13) local=col-1; }
                else if(row==3) { if(col<13) local=col; else if(col==14) local=13; }
                else if(row==4) { if(col<12) local=col; else if(col>=13) local=col-1; }
                else if(row<5) { if(col<Names[row].Length) local=col; }
                else { int[] columns={0,1,2,6,9,10,12,13,14}; local=Array.IndexOf(columns,col); }
                w.Write(local<0 ? uint.MaxValue : (uint)(led+local));
                if(col==14) led+=Names[row].Length;
            }
            w.Write((ushort)80); for(int i=0;i<80;i++) { Str(w,LedNames[i]); w.Write((uint)Hardware[i]); }
            w.Write((ushort)80); for(int i=0;i<80;i++) w.Write(snapshot==null ? 0u : snapshot[i]);
            long length=m.Length; m.Position=0; w.Write((uint)length); w.Flush(); return m.ToArray();
        }
    }
    public void Dispose() {
        lock(gate) { enabled=false; running=false; } listener.Stop();
        Client[] copy; lock(clientsGate) copy=clients.ToArray(); foreach(Client c in copy) c.Socket.Close();
        if(writer!=null) writer.Join(3000); if(acceptor!=null) acceptor.Join(1000); Log("Stopped.");
    }
    static uint RGB(Color c) { return (uint)(c.R | c.G<<8 | c.B<<16); }
    [STAThread] static void Main(string[] args) {
        if(Array.IndexOf(args,"--self-test")>=0) { F75BridgeTests.Run(); return; }
        if(Array.IndexOf(args,"--runtime-test")>=0) { F75BridgeTests.RunRuntime(); return; }
        if(Array.IndexOf(args,"--export-icon")>=0) { using(Icon icon=F75Experience.MakeIcon())using(FileStream file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75.ico")))icon.Save(file);return; }
        bool mock=Array.IndexOf(args,"--mock")>=0;
        bool headless=Array.IndexOf(args,"--headless")>=0;
        bool startMinimized=Array.IndexOf(args,"--start-minimized")>=0;
        bool owner;string instance=mock?"F75WirelessBridgePreview02":"F75WirelessBridge02";
        if(Array.IndexOf(args,"--exit")>=0){try{using(EventWaitHandle quit=EventWaitHandle.OpenExisting("Local\\"+instance+"Quit"))quit.Set();}catch(WaitHandleCannotBeOpenedException){}return;}
        using(Mutex singleton=new Mutex(true,"Local\\"+instance,out owner)) {
            if(!owner){try{using(EventWaitHandle wake=EventWaitHandle.OpenExisting("Local\\"+instance+"Show"))wake.Set();}catch(WaitHandleCannotBeOpenedException){}return;}
            using(EventWaitHandle show=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\"+instance+"Show"))
            using(EventWaitHandle quit=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\"+instance+"Quit"))
        try { using(F75WirelessBridge bridge=new F75WirelessBridge(mock)) {
            if(Array.IndexOf(args,"--start-paused")>=0)bridge.Pause(true);
            if(headless) { Console.WriteLine("F75 bridge listening on 127.0.0.1:"+Port+" (mock="+mock+")"); while(!quit.WaitOne(1000)){} return; }
            F75Experience.Run(bridge,startMinimized,mock,show,quit);
        }} catch(Exception ex) { if(headless) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } else MessageBox.Show(ex.Message,"F75 wireless companion"); }
        }
    }
}

