// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Drawing;
using System.Net;
using System.Text.Json;
using System.Windows.Forms;

namespace KasaTapoConfigurationRepair;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            if(args.Length==2&&args[0]=="--preflight")
            {
                string Env(string name)=>Environment.GetEnvironmentVariable(name)??throw new ArgumentException("Connection setting missing.");
                var identity=new ProcessorIdentity(Env("CRESTRON_HOME_HOST"),Env("CRESTRON_HOME_CERT_SHA256"),Env("CRESTRON_HOME_SSH_FINGERPRINT"));
                Task.Run(async()=>{await using var client=new ProcessorAccess(identity,new(Env("CRESTRON_HOME_USER"),Env("CRESTRON_HOME_PASSWORD")));
                    var snapshot=await client.Snapshot();var review=RepairModel.Inspect(snapshot);
                    File.WriteAllText(args[1],JsonSerializer.Serialize(new{Version="1.0.0",ReadOnly=true,snapshot.HomeVersion,EligibleDevices=review.Children.Length,
                        Models=review.Children.Select(c=>c.Model).ToArray()},new JsonSerializerOptions{WriteIndented=true}));}).GetAwaiter().GetResult();
                return 0;
            }
            using var form=new RepairForm();
            if(args.Length==2&&args[0]=="--preview")
            {
                form.Preview();form.ShowInTaskbar=false;
                typeof(Control).GetMethod("CreateControl",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,[typeof(bool)],null)!.Invoke(form,[true]);form.PerformLayout();
                using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
                bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);return 0;
            }
            if(args.Length!=0)throw new ArgumentException("Unsupported option.");
            Application.Run(form);return 0;
        }
        catch(Exception error)
        {
            // Exception messages / HTTP responses may contain secrets. Report only their type.
            if(args.Length==2)File.WriteAllText(args[1]+".error.json",JsonSerializer.Serialize(new{ErrorType=error.GetType().Name,Stack=args[0]=="--preflight"?error.StackTrace:null}));
            else MessageBox.Show("The utility could not start ("+error.GetType().Name+"). Contact support.","Kasa/Tapo Configuration Repair");
            return 1;
        }
    }
}
internal sealed class RepairForm:Form
{
    private readonly TextBox host=new(),user=new(){Text="admin"},password=new(){UseSystemPasswordChar=true};
    private readonly Button inspect=new(){Text="1. Inspect devices",AutoSize=true},repair=new(){Text="2. Repair and reboot",AutoSize=true,Enabled=false},
        recover=new(){Text="Recover an interrupted repair...",AutoSize=true},open=new(){Text="Open report folder",AutoSize=true,Enabled=false};
    private readonly ListView devices=new(){View=View.Details,FullRowSelect=true,GridLines=true,Dock=DockStyle.Fill};
    private readonly TextBox status=new(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=SystemColors.Control};
    private readonly CheckBox consent=new(){Text="I understand that repair or recovery stops Crestron Home and reboots the whole processor.",AutoSize=true};
    private ProcessorIdentity? processor;private Review? review;private Snapshot? snapshot;private bool busy;private string? reportFolder;
    internal RepairForm()
    {
        Text="Kasa/Tapo Configuration Repair 1.0.0 — customer test";Font=new Font("Segoe UI",10);
        ClientSize=new Size(850,695);MinimumSize=new Size(866,734);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=2,RowCount=10};
        layout.ColumnStyles.Add(new(SizeType.Absolute,160));layout.ColumnStyles.Add(new(SizeType.Percent,100));
        void Wide(Control c,int row,SizeType size,float height){layout.Controls.Add(c,0,row);layout.SetColumnSpan(c,2);layout.RowStyles.Add(new(size,height));}
        Wide(new Label{Text="Repair missing saved records while keeping device IDs, rooms, scenes and programming.",AutoSize=true},0,SizeType.Absolute,32);
        void Field(string name,TextBox box,int row){layout.Controls.Add(new Label{Text=name,AutoSize=true,Anchor=AnchorStyles.Left},0,row);box.Dock=DockStyle.Fill;layout.Controls.Add(box,1,row);layout.RowStyles.Add(new(SizeType.Absolute,38));}
        Field("Processor address",host,1);Field("Administrator name",user,2);Field("Password",password,3);
        Wide(new Label{Text="For Home 4.012.0194 with KasaTapoPlatform 2.001.0002.4 only. Supports affected HS220, HS200 and S515 records. Run on the same trusted network as the processor.",Dock=DockStyle.Fill},4,SizeType.Absolute,58);
        var inspectRow=new FlowLayoutPanel{Dock=DockStyle.Fill};inspectRow.Controls.Add(inspect);Wide(inspectRow,5,SizeType.Absolute,43);
        devices.Columns.Add("Device",340);devices.Columns.Add("Model",140);devices.Columns.Add("Room",260);Wide(devices,6,SizeType.Percent,100);
        Wide(consent,7,SizeType.Absolute,36);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.AddRange([repair,recover,open]);Wide(actions,8,SizeType.Absolute,44);
        Wide(status,9,SizeType.Absolute,98);Controls.Add(layout);
        status.Text="Inspect is read-only. Your administrator login password is not saved. Private configuration backups are encrypted for your Windows account; only Support-report.json should be sent to support.";
        host.TextChanged+=(_,_)=>InvalidateReview();user.TextChanged+=(_,_)=>InvalidateReview();password.TextChanged+=(_,_)=>InvalidateReview();
        consent.CheckedChanged+=(_,_)=>UpdateButtons();inspect.Click+=async(_,_)=>await Inspect();repair.Click+=async(_,_)=>await RunRepair(false);
        recover.Click+=async(_,_)=>await RunRepair(true);
        open.Click+=(_,_)=>{if(reportFolder!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe"){Arguments="\""+reportFolder+"\"",UseShellExecute=true});};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;MessageBox.Show(this,"Please keep the utility open until the operation finishes or reports that recovery is needed.",Text);}};
    }
    private void InvalidateReview(){review=null;snapshot=null;processor=null;devices.Items.Clear();UpdateButtons();}
    private void UpdateButtons(){inspect.Enabled=!busy;repair.Enabled=!busy&&consent.Checked&&review?.Children.Length>0;recover.Enabled=!busy;open.Enabled=!busy&&reportFolder!=null;host.Enabled=user.Enabled=password.Enabled=consent.Enabled=!busy;}
    private void Status(string message){if(InvokeRequired){BeginInvoke(()=>Status(message));return;}status.Text=message;}
    private NetworkCredential Credentials()=>new(user.Text.Trim(),password.Text);
    private async Task Inspect()
    {
        busy=true;UpdateButtons();
        try
        {
            if(string.IsNullOrWhiteSpace(user.Text)||password.Text.Length==0)throw new ArgumentException();
            Status("Reading processor identities without sending your credentials...");string address=host.Text.Trim();
            var pinned=await Task.Run(()=>ProcessorAccess.Probe(address));
            string message=$"Processor: {pinned.Host}\n\nSSH fingerprint:\n{pinned.SshFingerprint}\n\nHTTPS certificate SHA-256:\n{pinned.Certificate}\n\nConfirm this is your processor on your trusted local network. Credentials will be sent only to these pinned identities. Continue with read-only inspection?";
            if(MessageBox.Show(this,message,"Confirm processor identity",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
            Status("Inspecting saved configuration and matching device bindings...");var credentials=Credentials();
            snapshot=await Task.Run(async()=>{await using var access=new ProcessorAccess(pinned,credentials);return await access.Snapshot();});
            review=RepairModel.Inspect(snapshot);processor=pinned;devices.Items.Clear();
            for(int i=0;i<review.Children.Length;i++){var c=review.Children[i];devices.Items.Add(new ListViewItem([c.Name,c.Model,review.RoomNames[i]]));}
            Status(review.Children.Length==0?"No eligible missing records were found. No changes made. Contact support if devices are still offline.":$"Found {review.Children.Length} affected devices. Review the list, then tick the reboot acknowledgement and choose Repair. No changes made yet.");
        }
        catch(Exception error){review=null;snapshot=null;processor=null;Status("Inspection could not complete ("+error.GetType().Name+"). No settings were changed. Check the address/login and supported Home/driver versions, or contact support.");}
        finally{busy=false;UpdateButtons();}
    }
    private async Task RunRepair(bool recovery)
    {
        if(!consent.Checked){MessageBox.Show(this,"Please tick the acknowledgement before recovery.",Text);return;}
        RepairState state;PrivateJournal journal;
        if(recovery)
        {
            using var picker=new FolderBrowserDialog{Description="Select the run folder containing the encrypted .dpapi recovery files",InitialDirectory=BaseFolder()};
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            try{journal=new(picker.SelectedPath);state=journal.Load();}
            catch(Exception error){Status("Recovery file unavailable ("+error.GetType().Name+"). Use the same Windows account and contact support.");return;}
            if(host.Text.Trim()!=state.Processor.Host){Status("Enter the same processor address as the selected recovery run before continuing.");return;}
        }
        else
        {
            if(processor is null||review is null||snapshot is null)return;
            state=new(){Processor=processor,Review=review,Baseline=snapshot,Owner=Guid.NewGuid().ToString("N")};
            journal=new(Path.Combine(BaseFolder(),DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+state.Owner[..8]));
        }
        string confirmation=recovery?"Restore the original saved records for this interrupted run?":"Add the missing configuration records for the listed "+state.Review.Children.Length+" devices?";
        if(MessageBox.Show(this,confirmation+"\n\nProcessor: "+state.Processor.Host+"\nCrestron Home and every integration on it will be temporarily unavailable during a full processor reboot. Allow up to 8 minutes for startup. Keep this utility open.",Text,MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
        var credentials=Credentials();reportFolder=journal.Folder;busy=true;UpdateButtons();
        try
        {
            await Task.Run(async()=>{await using var access=new ProcessorAccess(state.Processor,credentials);
                if(recovery)await RepairWorkflow.Recover(access,journal,state,Status);else await RepairWorkflow.Apply(access,journal,state,Status);});
            try{journal.Report(state,recovery?"OriginalRestored":"RepairVerified");}
            catch(IOException){Status("Operation verified, but the support report could not be saved. Keep the encrypted recovery folder and contact support.");}
            review=null;
        }
        catch(Exception error)
        {
            try{journal.Report(state,"Incomplete",error.GetType().Name);}catch(IOException){}
            Status(state.Stage=="Unchanged"?"Preflight did not pass ("+error.GetType().Name+"). Home was not stopped, no configuration was changed and the reservation was released. Send Support-report.json.":
                "Operation incomplete ("+error.GetType().Name+") at "+state.Stage+". Do not repeat Repair. Keep the recovery files and send Support-report.json. Use Recover an interrupted repair to restore the original records. Home may be stopped or still starting.");
            review=null;
        }
        finally{busy=false;UpdateButtons();}
    }
    private static string BaseFolder()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NeilColvin","KasaTapoConfigurationRepair");
    internal void Preview()
    {
        host.Text="192.168.1.100";devices.Items.Add(new ListViewItem(["Ceiling lights","HS220(US)","Living room"]));
        devices.Items.Add(new ListViewItem(["Island lights","HS220(US)","Kitchen"]));devices.Items.Add(new ListViewItem(["Hall lights","S515(US)","Hall"]));
        status.Text="Preview only. Inspect lists the affected devices before repair. A real repair retains the existing IDs and user setup, and adds the two missing configuration records.";
    }
}
