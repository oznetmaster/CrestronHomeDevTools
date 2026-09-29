// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;
using CrestronHomeDevTools.Automation;

namespace CrestronHomeDevTools.Setup;

/// <summary>Runs in the signed-in desktop session; the service never opens desktop windows.</summary>
internal sealed class OperatorInboxContext:ApplicationContext
{
 private readonly Func<IReadOnlyList<SubmissionOperatorInbox>> _inboxes;
 private readonly bool _persistent;
 private readonly System.Windows.Forms.Timer _timer=new() {Interval=2000};
 private readonly NotifyIcon _tray=new() {Icon=SystemIcons.Information,Text="Crestron submission actions",Visible=true};
 private readonly Dictionary<string,OperatorWindow> _windows=new(StringComparer.Ordinal);
 private readonly HashSet<string> _shown=new(StringComparer.Ordinal);
 private readonly Dictionary<string,WorkflowAlertWindow> _alerts=new(StringComparer.Ordinal);
 private HashSet<string> _activeAlerts=new(StringComparer.Ordinal);
 private Func<IReadOnlyList<SubmissionOperatorAlert>>? _readAlerts;
 private SubmissionAlertAcknowledgements? _dismissals;
 private bool _alertReadFailed;
 private bool _failed;
 public bool Completed {get;private set;}
 public OperatorInboxContext(string directory,string runKey):this(()=>[new(directory,runKey)],false) { }
 public OperatorInboxContext(string registry,string[] profiles,string? workerStatus=null):this(()=>SubmissionOperatorDiscovery.Read(registry,profiles),true) {
  SubmissionOperatorDiscovery.ValidateProfiles(profiles);
  if(!Path.IsPathFullyQualified(registry))throw new ArgumentException("Use an absolute private registry.");
  if(workerStatus!=null) {
   if(!Path.IsPathFullyQualified(workerStatus))throw new ArgumentException("Use an absolute private worker status directory.");
   _readAlerts=()=>SubmissionOperatorAlerts.Read(registry,profiles,workerStatus);
   string identity=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
    Path.GetFullPath(registry)+"\n"+string.Join(",",profiles.OrderBy(p=>p,StringComparer.Ordinal))+"\n"+Path.GetFullPath(workerStatus))));
   _dismissals=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CrestronHomeDevTools","OperatorAlerts",identity+".json"));
  }
 }
 private OperatorInboxContext(Func<IReadOnlyList<SubmissionOperatorInbox>> inboxes,bool persistent) {
  _inboxes=inboxes;_persistent=persistent;
  if(persistent)_timer.Interval=10000;
  var menu=new ContextMenuStrip();menu.Items.Add("Show pending actions",null,(_,_)=>Check(true));
  menu.Items.Add("Exit action monitor",null,(_,_)=>ExitThread());_tray.ContextMenuStrip=menu;
  _tray.DoubleClick+=(_,_)=>Check(true);
  _timer.Tick+=(_,_)=>Check(false);_timer.Start();
 }
 private void Check(bool reopen) {
  try {
   var pending=new List<SubmissionOperatorHandle>();
   foreach(var inbox in _inboxes()) {
    if(SubmissionOperatorInboxLifecycle.IsClosed(inbox)) {
     if(!_persistent) {Completed=true;ExitThread();return;}
     continue;
    }
    // Release registration precedes the first request. An absent future inbox is normal.
    if(_persistent) {
     try {
      if((File.GetAttributes(inbox.Directory)&FileAttributes.Directory)==0)throw new InvalidDataException("Operator inbox is not a directory.");
     } catch(DirectoryNotFoundException) {continue;}
       catch(FileNotFoundException) {continue;}
    }
    pending.AddRange(SubmissionOperatorStep.Pending(inbox.Directory,inbox.RunKey));
   }
   var active=pending.Select(h=>h.RequestSha256).ToHashSet(StringComparer.Ordinal);
   foreach(var entry in _windows.ToArray())if(!active.Contains(entry.Key))entry.Value.Close();
   _shown.IntersectWith(active);
   _failed=false;_tray.Text=pending.Count==0?"Crestron: no physical action needed":"Crestron: physical action needed";
   foreach(var handle in pending) {
    if(_windows.TryGetValue(handle.RequestSha256,out var existing)) {if(reopen)existing.Activate();continue;}
    if(!reopen && !_shown.Add(handle.RequestSha256))continue;
    _shown.Add(handle.RequestSha256);
    var window=new OperatorWindow(handle);_windows.Add(handle.RequestSha256,window);
    window.FormClosed+=(_,_)=>_windows.Remove(handle.RequestSha256);window.Show();
   }
   CheckAlerts(reopen);
  } catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException) {
   foreach(var window in _windows.Values.ToArray())window.Close();
   _shown.Clear();
   _tray.Text="Crestron: action inbox unavailable";
   if(!_failed) {_tray.ShowBalloonTip(10000,"Action inbox unavailable","Cannot read the private action inbox. Check its connection and records; no test action is implied.",ToolTipIcon.Warning);_failed=true;}
  }
 }
 private void CheckAlerts(bool reopen) {
  if(_readAlerts==null || _dismissals==null)return;
  try {
   var alerts=_readAlerts();
   _activeAlerts=alerts.Select(a=>a.Key).ToHashSet(StringComparer.Ordinal);
   _dismissals.RetainActive(_activeAlerts);
   foreach(var entry in _alerts.ToArray())if(!_activeAlerts.Contains(entry.Key))entry.Value.Close();
   _alertReadFailed=false;
   if(alerts.Count>0)_tray.Text="Crestron: workflow notice available";
   foreach(var alert in alerts) {
    if(_alerts.TryGetValue(alert.Key,out var existing)){if(reopen)existing.Activate();continue;}
    if(!reopen && _dismissals.IsDismissed(alert.Key))continue;
    var window=new WorkflowAlertWindow(alert);_alerts.Add(alert.Key,window);
    window.FormClosed+=(_,_)=>{
     _alerts.Remove(alert.Key);
     if(_activeAlerts.Contains(alert.Key)) {
      try {_dismissals.Dismiss(alert.Key);}
      catch(Exception e) when(e is IOException or UnauthorizedAccessException){_tray.ShowBalloonTip(10000,"Notice dismissal not saved","The notice may appear again after restarting the action monitor.",ToolTipIcon.Warning);}
     }
    };
    window.Show();
   }
  }catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException) {
   if(!_alertReadFailed){_tray.ShowBalloonTip(10000,"Workflow status unavailable","Cannot read the worker's private status. Check the worker connection; no restart or test action is implied.",ToolTipIcon.Warning);_alertReadFailed=true;}
  }
 }
 protected override void Dispose(bool disposing) {
  if(disposing) {
   _timer.Dispose();foreach(var window in _windows.Values.ToArray())window.Dispose();
   _activeAlerts.Clear();foreach(var window in _alerts.Values.ToArray())window.Dispose();
   _tray.Visible=false;_tray.ContextMenuStrip?.Dispose();_tray.Dispose();
  }
  base.Dispose(disposing);
 }
}
