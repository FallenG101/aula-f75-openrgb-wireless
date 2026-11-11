// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

static class F75BridgeTests
{
    internal static void RunRuntime() {
        try {
            List<double> reportTimes=new List<double>();string timing="";
            using(F75WirelessBridge bridge=new F75WirelessBridge(true,0,delegate(double t){lock(reportTimes)reportTimes.Add(t);})) {
                bridge.Smooth=true;bridge.Solid(255);System.Threading.Thread.Sleep(400);
                Check(bridge.Reports>0 && bridge.Reports<=5,"solid pacing at most 10 fps");
                bridge.Pause(true);long stopped=bridge.Reports;
                using(System.Net.Sockets.TcpClient client=new System.Net.Sockets.TcpClient("127.0.0.1",bridge.ListeningPort)) {
                    byte[] single=new byte[24];single[0]=79;single[1]=82;single[2]=71;single[3]=66;
                    Array.Copy(BitConverter.GetBytes((uint)1052),0,single,8,4);single[12]=8;single[20]=33;
                    client.GetStream().Write(single,0,single.Length);
                    System.Threading.Thread.Sleep(200);
                    Check(bridge.Updates==2,"SDK update accepted while paused");
                    Check(bridge.Reports==stopped,"pause survives incoming SDK updates without lighting writes");
                    bridge.Pause(false);System.Threading.Thread.Sleep(200);Check(bridge.Reports>stopped,"resume restarts lighting");
                }
                bridge.Pause(true);bridge.Smooth=false;
                uint[] many=new uint[80];for(int k=0;k<80;k++)many[k]=(uint)(k*197381+1);
                bridge.SetColors(many,"runtime test");bridge.Pause(false);
                System.Threading.Thread.Sleep(50);bridge.Pause(true);stopped=bridge.Reports;
                System.Threading.Thread.Sleep(100);Check(stopped==bridge.Reports,"pause stops an in-flight multipart frame");
                bridge.Smooth=true;lock(reportTimes)reportTimes.Clear();
                bridge.SetColors(many,"paced full-frame test");bridge.Pause(false);
                System.Threading.Thread.Sleep(1600);bridge.Pause(true);
                lock(reportTimes){
                    Check(reportTimes.Count>=8,"multipart frame exercised");
                    for(int k=1;k<reportTimes.Count;k++)Check(reportTimes[k]-reportTimes[k-1]>=F75Pacing.ReportGapMs,"no short report bursts");
                    for(int k=8;k<reportTimes.Count;k+=8)Check(reportTimes[k]-reportTimes[k-1]>=F75Pacing.FrameQuietMs,"quiet between frames");
                    if(reportTimes.Count>8)Check(reportTimes[8]-reportTimes[0]>=F75Pacing.FrameInterval(8),"sustained report budget enforced");
                    if(reportTimes.Count>16){
                        double minGap=double.MaxValue;for(int k=1;k<reportTimes.Count;k++)minGap=Math.Min(minGap,reportTimes[k]-reportTimes[k-1]);
                        int frames=(reportTimes.Count-1)/8;double frameMs=(reportTimes[frames*8]-reportTimes[0])/frames;
                        timing=String.Format(System.Globalization.CultureInfo.InvariantCulture,"Mock-only timing: minimum report gap {0:F2}ms; mean full-frame interval {1:F2}ms ({2:F2} frames/s). Not a hardware measurement.\r\n",minGap,frameMs,1000/frameMs);
                    }
                }
            }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75-runtime-test.txt"),"PASS: "+checks+" runtime checks; mock SDK, pacing, pause and resume. No HID access performed.\r\n"+timing);
        } catch(Exception ex) {File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75-runtime-test.txt"),ex.ToString());Environment.ExitCode=1;}
    }
    static int checks;
    static void Check(bool condition,string message) { checks++; if(!condition) throw new Exception("FAIL: "+message); }
    static string Str(BinaryReader r) { int n=r.ReadUInt16(); byte[] b=r.ReadBytes(n); Check(b.Length==n && b[n-1]==0,"terminated SDK string"); return Encoding.UTF8.GetString(b,0,n-1); }
    static void ReadDescription(byte[] data,uint version) {
        using(BinaryReader r=new BinaryReader(new MemoryStream(data))) {
            Check(r.ReadUInt32()==data.Length,"descriptor size prefix"); Check(r.ReadUInt32()==5,"keyboard type");
            Check(Str(r).Contains("F75"),"device name"); if(version>=1) Check(Str(r)=="AULA","vendor");
            Str(r); Str(r); Str(r); Str(r);
            Check(r.ReadUInt16()==1 && r.ReadUInt32()==0,"one active mode"); Check(Str(r)=="Direct","direct name");
            Check(r.ReadUInt32()==0 && r.ReadUInt32()==32,"mode value and per-key capability");
            r.ReadUInt32(); r.ReadUInt32(); if(version>=3) { r.ReadUInt32(); r.ReadUInt32(); }
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); if(version>=3) r.ReadUInt32();
            r.ReadUInt32(); Check(r.ReadUInt32()==1 && r.ReadUInt16()==0,"per-key color mode");
            Check(r.ReadUInt16()==1 && Str(r)=="Keyboard" && r.ReadUInt32()==2,"one matrix zone");
            Check(r.ReadUInt32()==80 && r.ReadUInt32()==80 && r.ReadUInt32()==80,"zone fixed count");
            Check(r.ReadUInt16()==368 && r.ReadUInt32()==6 && r.ReadUInt32()==15,"matrix dimensions");
            HashSet<uint> mapped=new HashSet<uint>();
            for(int k=0;k<90;k++) { uint index=r.ReadUInt32(); if(index!=uint.MaxValue) Check(index<80 && mapped.Add(index),"unique in-range matrix index");
                if(k==1 || k==58 || k==72) Check(index==uint.MaxValue,"keyboard gaps"); }
            Check(mapped.Count==80,"all keys mapped"); Check(r.ReadUInt16()==80,"80 named LEDs");
            for(int k=0;k<80;k++) { Check(Str(r)==F75WirelessBridge.LedNames[k],"LED order"); Check(r.ReadUInt32()==F75WirelessBridge.Hardware[k],"hardware index"); }
            Check(r.ReadUInt16()==80,"80 colors"); for(int k=0;k<80;k++) r.ReadUInt32(); Check(r.BaseStream.Position==data.Length,"no trailing/missing description data");
        }
    }
    static Dictionary<byte,uint> Decode(List<byte[]> messages) {
        Dictionary<byte,uint> output=new Dictionary<byte,uint>();
        foreach(byte[] payload in messages) {
            List<byte[]> frames=F75Wire.Frame(payload); Check(frames.Count<=29,"bounded chunk count"); List<byte> reconstructed=new List<byte>();
            for(int k=0;k<frames.Count;k++) { byte[] f=frames[k]; Check(f.Length==20 && f[0]==0x13 && f[1]==0x88 && f[2]==frames.Count && f[3]==k && F75Wire.ChecksumValid(f),"wire header/checksum");
                for(int j=0;j<f[4]-0x10;j++) reconstructed.Add(f[5+j]); }
            byte[] p=reconstructed.ToArray(); Check(p.Length==payload.Length,"chunk reassembly");
            for(int k=0;k<p.Length;) { Check(k+4<=p.Length,"group header bounds"); uint color=(uint)(p[k]|p[k+1]<<8|p[k+2]<<16); int count=p[k+3]; k+=4;
                Check(count>0 && k+count<=p.Length,"group count bounds");
                for(int j=0;j<count;j++) { byte key=p[k++]; Check(!output.ContainsKey(key),"key appears once"); output.Add(key,color); }
            }
        } return output;
    }
    internal static void Run() {
        try {
            for(int reports=1;reports<=29;reports++){Check(F75Pacing.FrameInterval(reports)>=100,"frame ceiling 10 fps");Check(F75Pacing.FrameInterval(reports)>=reports*1000.0/60.0,"sustained budget 60 reports per second");}
            Check(F75WirelessBridge.Hardware.Length==80 && F75WirelessBridge.LedNames.Length==80,"key count");
            HashSet<byte> unique=new HashSet<byte>(F75WirelessBridge.Hardware); Check(unique.Count==80,"hardware IDs unique");
            uint[] c=new uint[80]; for(int k=0;k<80;k++) c[k]=0xFF;
            List<byte[]> solid=F75Wire.Messages(c,null,F75WirelessBridge.Hardware);
            Check(solid.Count==1 && solid[0].Length==84 && F75Wire.Frame(solid[0]).Count==6,"full-board solid sizing");
            Dictionary<byte,uint> decoded=Decode(solid); Check(decoded.Count==80,"all keys included in solid");
            for(int k=0;k<80;k++) c[k]=(uint)(k*661+1);
            List<byte[]> distinct=F75Wire.Messages(c,null,F75WirelessBridge.Hardware);
            Check(distinct.Count==1 && distinct[0].Length==400 && F75Wire.Frame(distinct[0]).Count==29,"80 colors are one atomic message");
            decoded=Decode(distinct);
            for(int k=0;k<80;k++) Check(decoded[F75WirelessBridge.Hardware[k]]==c[k],"80 distinct colors round trip");
            Check(F75Wire.Messages(c,c,F75WirelessBridge.Hardware).Count==0,"unchanged delta is empty");
            Check(Decode(F75WirelessBridge.Refresh(c,false,80)).Count==80,"idle refresh includes every key");
            Check(Decode(F75WirelessBridge.Refresh(c,true,0)).Count==80,"changed refresh includes every key");
            Check(F75WirelessBridge.Refresh(c,false,79).Count==0,"refresh rate bounded");
            uint[] d=(uint[])c.Clone(); d[30]=0xFFFF00;
            decoded=Decode(F75Wire.Messages(d,c,F75WirelessBridge.Hardware)); Check(decoded.Count==1 && decoded[F75WirelessBridge.Hardware[30]]==0xFFFF00,"single-key delta");
            byte[] cyan=F75Wire.Frame(new byte[]{0,255,255,1,0x37})[0];
            Check(BitConverter.ToString(cyan)=="13-88-01-00-15-00-FF-FF-01-37-00-00-00-00-00-00-00-00-00-E7","independent captured cyan example");
            for(uint v=0;v<=3;v++) ReadDescription(F75WirelessBridge.Description(v,false),v);
            uint[] result; Check(!F75WirelessBridge.TryColors(1052,new byte[7],c,out result),"truncated single rejected");
            byte[] single=new byte[8]; Array.Copy(BitConverter.GetBytes(80),single,4); Check(!F75WirelessBridge.TryColors(1052,single,c,out result),"out of range rejected");
            Array.Copy(BitConverter.GetBytes(3),single,4); single[4]=0x56; single[5]=0x34; single[6]=0x12;
            Check(F75WirelessBridge.TryColors(1052,single,c,out result) && result[3]==0x123456 && c[3]!=result[3],"single accepted without changing source");
            byte[] full=new byte[326]; Array.Copy(BitConverter.GetBytes(326),full,4); full[4]=80;
            for(int k=0;k<80;k++) Array.Copy(BitConverter.GetBytes(c[k]),0,full,6+k*4,4);
            Check(F75WirelessBridge.TryColors(1050,full,c,out result) && result[79]==c[79],"full RGB packet accepted");
            full[0]=0; Check(!F75WirelessBridge.TryColors(1050,full,c,out result),"wrong size prefix rejected");
            uint[] pattern=new uint[80]; pattern[0]=255; pattern[30]=65280; pattern[74]=16711680; pattern[79]=65535;
            List<byte[]> mapping=F75WirelessBridge.Refresh(pattern,true,0);
            Check(mapping.Count==1 && mapping[0].Length==100 && F75Wire.Frame(mapping[0]).Count==8,"four-key test is one eight-chunk message");
            bool rejected=false; try { F75Wire.Frame(new byte[401]); } catch(ArgumentException) { rejected=true; } Check(rejected,"oversized HID payload rejected");
            uint[] fade=new uint[80];
            for(int level=0;level<256;level++) {
                for(int k=0;k<80;k++) fade[k]=(uint)level;
                List<byte[]> compact=F75Render.Plan(fade,true);
                Check(compact.Count==1 && compact[0][4]==0x23 && compact[0][5]==level && F75Wire.ChecksumValid(compact[0]),"solid brightness preserved in compact packet");
                Check(F75Render.Plan(fade,false).Count==6,"exact fallback keeps complete solid frame");
            }
            for(int k=0;k<80;k++) fade[k]=(uint)(k*197381);
            List<byte[]> smooth=F75Render.Plan(fade,true);
            Check(smooth.Count<=8,"animation palette bounds full-frame report count");
            List<byte> payload=new List<byte>(); foreach(byte[] f in smooth)for(int k=0;k<f[4]-16;k++)payload.Add(f[k+5]);
            Check(Decode(new List<byte[]>{payload.ToArray()}).Count==80,"smooth frame retains all 80 keys");
            Random random=new Random(7502);
            for(int sample=0;sample<100;sample++) {
                for(int k=0;k<80;k++)fade[k]=k%9==0?0u:(uint)random.Next(1,0x1000000);
                smooth=F75Render.Plan(fade,true);Check(smooth.Count<=8,"random palette report bound");
                payload.Clear();foreach(byte[] f in smooth)for(int k=0;k<f[4]-16;k++)payload.Add(f[k+5]);
                decoded=Decode(new List<byte[]>{payload.ToArray()});Check(new HashSet<uint>(decoded.Values).Count<=8,"at most eight palette colors");
                for(int k=0;k<80;k++)Check((decoded[F75WirelessBridge.Hardware[k]]==0)==(fade[k]==0),"black and nonblack keys remain distinct");
            }
            smooth=F75Render.Plan(pattern,true);payload.Clear();foreach(byte[] f in smooth)for(int k=0;k<f[4]-16;k++)payload.Add(f[k+5]);
            decoded=Decode(new List<byte[]>{payload.ToArray()});for(int k=0;k<80;k++)Check(decoded[F75WirelessBridge.Hardware[k]]==pattern[k],"small palette colors unchanged");
            string report="PASS: "+checks+" checks; framing, mapping, SDK validation, all 256 solid brightness levels and reduced-palette coverage. No HID access performed.";
            Console.WriteLine(report); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75-self-test.txt"),report+Environment.NewLine);
        } catch(Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75-self-test.txt"),ex.ToString()); Environment.ExitCode=1; }
    }
}
