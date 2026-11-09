// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

static class F75BridgeTests
{
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
            string report="PASS: "+checks+" checks; packet encoding, captured example, 80-key mapping, SDK 0-3 serialization, malformed RGB rejection. No HID access performed.";
            Console.WriteLine(report); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75-self-test.txt"),report+Environment.NewLine);
        } catch(Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"F75-self-test.txt"),ex.ToString()); Environment.ExitCode=1; }
    }
}
