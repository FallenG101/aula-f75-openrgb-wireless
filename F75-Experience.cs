// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

static class F75Render {
    internal static List<byte[]> Plan(uint[] source,bool smooth) {
        if(source.Length!=80) throw new ArgumentException("80 keys required");
        bool solid=true; for(int i=1;i<80;i++) if(source[i]!=source[0]) { solid=false; break; }
        if(smooth && solid) {
            byte[] p=new byte[20]; p[0]=0x13;p[1]=0x88;p[2]=1;p[4]=0x23;
            p[5]=(byte)source[0];p[6]=(byte)(source[0]>>8);p[7]=(byte)(source[0]>>16);p[19]=F75Wire.Sum(p);
            return new List<byte[]>{p};
        }
        uint[] colors=(uint[])source.Clone();
        if(smooth) colors=Reduce(source);
        return F75Wire.Frame(F75Wire.Messages(colors,null,F75WirelessBridge.Hardware)[0]);
    }
    static int Distance(uint a,uint b) {
        int total=0;for(int s=0;s<24;s+=8){int d=(int)((a>>s)&255)-(int)((b>>s)&255);total+=d*d;}return total;
    }
    // Choose actual input colors, rather than snapping every channel to a coarse grid.
    // Black gets its own entry and is never assigned to a nonblack key.
    static uint[] Reduce(uint[] source) {
        HashSet<uint> unique=new HashSet<uint>(source);
        if(unique.Count<=8)return (uint[])source.Clone();
        List<uint> palette=new List<uint>();bool black=unique.Contains(0);
        if(black)palette.Add(0);else palette.Add(source[0]);
        while(palette.Count<8){
            int farthest=-1;uint chosen=0;
            foreach(uint c in source){int nearest=int.MaxValue;foreach(uint p in palette)nearest=Math.Min(nearest,Distance(c,p));if(nearest>farthest){farthest=nearest;chosen=c;}}
            palette.Add(chosen);
        }
        uint[] result=new uint[source.Length];
        for(int i=0;i<source.Length;i++){
            if(source[i]==0){result[i]=0;continue;}
            int best=black?1:0;
            for(int j=best+1;j<palette.Count;j++)if(Distance(source[i],palette[j])<Distance(source[i],palette[best]))best=j;
            result[i]=palette[best];
        }
        return result;
    }
}

static class F75Experience {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    static readonly Color Background=Color.Black,Surface=Color.FromArgb(18,20,23),Text=Color.FromArgb(236,238,242),Muted=Color.FromArgb(162,167,176),Accent=Color.FromArgb(102,221,204);
    static void DarkTitleBar(Form form) {
        // Supported DWM attributes; failures on older Windows leave its normal title bar.
        try {int dark=1,black=0,light=0xF2EEEC;DwmSetWindowAttribute(form.Handle,20,ref dark,4);DwmSetWindowAttribute(form.Handle,35,ref black,4);DwmSetWindowAttribute(form.Handle,36,ref light,4);}
        catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
    }
    sealed class DarkMenuColors:ProfessionalColorTable {
        public override Color ToolStripDropDownBackground {get{return Surface;}}
        public override Color MenuBorder {get{return Color.FromArgb(52,58,64);}}
        public override Color MenuItemBorder {get{return Color.FromArgb(52,58,64);}}
        public override Color MenuItemSelected {get{return Color.FromArgb(32,37,42);}}
        public override Color MenuItemSelectedGradientBegin {get{return MenuItemSelected;}}
        public override Color MenuItemSelectedGradientEnd {get{return MenuItemSelected;}}
        public override Color SeparatorDark {get{return Color.FromArgb(52,58,64);}}
        public override Color SeparatorLight {get{return Surface;}}
    }
    internal static Icon MakeIcon() {
        using(Bitmap b=new Bitmap(64,64)) {
            using(Graphics g=Graphics.FromImage(b)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
                using(SolidBrush fill=new SolidBrush(Color.FromArgb(20,31,48)))g.FillEllipse(fill,1,1,62,62);
                using(Pen edge=new Pen(Color.FromArgb(75,220,207),3))g.DrawRectangle(edge,12,20,40,26);
                using(SolidBrush keys=new SolidBrush(Color.White)) {
                    for(int y=26;y<39;y+=7)for(int x=18;x<49;x+=7)g.FillRectangle(keys,x,y,4,4);
                    g.FillRectangle(keys,24,40,17,3);
                }
            }
            IntPtr h=b.GetHicon();try{return (Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}
        }
    }
    static Button Button(string title,Action action) {
        Button b=new Button{Text=title,AutoSize=true,Height=36,FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,BackColor=Surface,ForeColor=Text,Margin=new Padding(0,0,10,8),Padding=new Padding(12,5,12,5)};
        b.FlatAppearance.BorderSize=1;b.FlatAppearance.BorderColor=Color.FromArgb(52,58,64);b.FlatAppearance.MouseOverBackColor=Color.FromArgb(32,37,42);b.FlatAppearance.MouseDownBackColor=Color.FromArgb(41,48,54);b.Click+=delegate{action();};return b;
    }
    internal static void Run(F75WirelessBridge bridge,bool minimized,bool mock,System.Threading.EventWaitHandle showRequest,System.Threading.EventWaitHandle quitRequest) {
        Application.EnableVisualStyles();
        using(Icon icon=MakeIcon()) using(Form f=new Form()) {
            f.Text=mock?"F75 • Preview":"F75 • Wireless lighting";f.Icon=icon;f.ClientSize=new Size(600,420);f.MinimumSize=new Size(600,460);f.StartPosition=FormStartPosition.CenterScreen;
            f.BackColor=Background;f.ForeColor=Text;f.Font=new Font("Segoe UI",10);f.HandleCreated+=delegate{DarkTitleBar(f);};
            TableLayoutPanel page=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=7};f.Controls.Add(page);
            Label title=new Label{Text="F75",Font=new Font("Segoe UI",26,FontStyle.Bold),AutoSize=true,ForeColor=Text};
            page.Controls.Add(title);page.Controls.Add(new Label{Text="Wireless lighting companion  ·  v0.2.1",AutoSize=true,ForeColor=Muted});
            Label status=new Label{AutoSize=false,Dock=DockStyle.Fill,Height=76,Padding=new Padding(0,12,0,6),ForeColor=Accent};page.Controls.Add(status);
            bool exiting=false;
            FlowLayoutPanel actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};
            actions.Controls.Add(Button("Choose color",delegate{using(ColorDialog d=new ColorDialog())if(d.ShowDialog(f)==DialogResult.OK)bridge.Solid((uint)(d.Color.R|d.Color.G<<8|d.Color.B<<16));}));
            Button pause=null;pause=Button("Pause lighting",delegate{bridge.Pause(!bridge.Paused);pause.Text=bridge.Paused?"Resume lighting":"Pause lighting";});actions.Controls.Add(pause);
            actions.Controls.Add(Button("Exit",delegate{exiting=true;f.Close();}));page.Controls.Add(actions);
            CheckBox smooth=new CheckBox{Text="Smooth rendering (experimental)",AutoSize=true,FlatStyle=FlatStyle.Flat,ForeColor=Text,BackColor=Background,Padding=new Padding(0,8,0,8)};
            string settings=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,mock?"F75-preview-settings.txt":"F75-settings.txt");
            try{smooth.Checked=File.Exists(settings)&&File.ReadAllText(settings).Trim()=="smooth";}catch(IOException){}catch(UnauthorizedAccessException){}
            bridge.Smooth=smooth.Checked;
            smooth.CheckedChanged+=delegate{bridge.Smooth=smooth.Checked;try{File.WriteAllText(settings,smooth.Checked?"smooth":"exact");}catch(IOException){}catch(UnauthorizedAccessException){}};page.Controls.Add(smooth);
            page.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(540,0),ForeColor=Muted,Text="Smooth mode reduces animation color detail and uses a compact solid-color command. Switch it off if colors flicker.\nOpenRGB connects to 127.0.0.1 : 6743. Close this window to keep lighting in the tray."});
            Label stats=new Label{AutoSize=true,Padding=new Padding(0,12,0,0),ForeColor=Muted};page.Controls.Add(stats);
            using(ContextMenuStrip menu=new ContextMenuStrip()) using(NotifyIcon tray=new NotifyIcon{Icon=icon,Text="F75 • Wireless lighting",Visible=true,ContextMenuStrip=menu}) {
                menu.BackColor=Surface;menu.ForeColor=Text;menu.ShowImageMargin=false;menu.Renderer=new ToolStripProfessionalRenderer(new DarkMenuColors()){RoundedEdges=false};
                Action hide=delegate{f.Hide();f.ShowInTaskbar=false;};Action show=delegate{f.ShowInTaskbar=true;f.Show();f.WindowState=FormWindowState.Normal;f.Activate();};
                menu.Items.Add("Open F75",null,delegate{show();});
                ToolStripMenuItem trayPause=new ToolStripMenuItem("Pause lighting");trayPause.Click+=delegate{pause.PerformClick();trayPause.Text=bridge.Paused?"Resume lighting":"Pause lighting";};menu.Items.Add(trayPause);
                menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Exit",null,delegate{exiting=true;f.Close();});tray.DoubleClick+=delegate{show();};
                f.Resize+=delegate{if(f.WindowState==FormWindowState.Minimized)hide();};f.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(!exiting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;hide();}};
                if(minimized){f.WindowState=FormWindowState.Minimized;f.ShowInTaskbar=false;f.Shown+=delegate{hide();};}
                using(Process process=Process.GetCurrentProcess()) using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=1000}) {
                    long reports=bridge.Reports;TimeSpan cpu=process.TotalProcessorTime;Stopwatch elapsed=Stopwatch.StartNew();
                    timer.Tick+=delegate {
                        if(quitRequest.WaitOne(0)){exiting=true;f.Close();return;}
                        if(showRequest.WaitOne(0))show();
                        trayPause.Text=bridge.Paused?"Resume lighting":"Pause lighting";
                        double seconds=elapsed.Elapsed.TotalSeconds;elapsed.Restart();process.Refresh();TimeSpan next=process.TotalProcessorTime;
                        double percent=(next-cpu).TotalSeconds/Math.Max(.01,seconds)/Environment.ProcessorCount*100;cpu=next;
                        long now=bridge.Reports;status.Text=bridge.Status.Split(new[]{"\r\n"},StringSplitOptions.None)[0]+"\n"+(bridge.Paused?"Paused — radio lighting writes stopped":"OpenRGB lighting active when connected");
                        stats.Text=String.Format("{0:F1} reports/s  ·  {1:F1}% CPU  ·  {2:F0} MB memory",(now-reports)/Math.Max(.01,seconds),percent,process.WorkingSet64/1048576.0);reports=now;
                    };timer.Start();Application.Run(f);
                }
                tray.Visible=false;
            }
        }
    }
}
