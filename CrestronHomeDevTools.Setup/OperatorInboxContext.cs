// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;

namespace CrestronHomeDevTools.Setup;

/// <summary>Runs in the signed-in desktop session; the service never opens desktop windows.</summary>
internal sealed class OperatorInboxContext:ApplicationContext
{
 private readonly string _directory,_runKey;
 private readonly System.Windows.Forms.Timer _timer=new() {Interval=2000};
 private readonly NotifyIcon _tray=new() {Icon=SystemIcons.Information,Text="Crestron submission actions",Visible=true};
 private readonly Dictionary<string,OperatorWindow> _windows=new(StringComparer.Ordinal);
 private readonly HashSet<string> _shown=new(StringComparer.Ordinal);
 private bool _failed;
 public OperatorInboxContext(string directory,string runKey) {
  _directory=directory;_runKey=runKey;
  var menu=new ContextMenuStrip();menu.Items.Add("Show pending actions",null,(_,_)=>Check(true));
  menu.Items.Add("Exit action monitor",null,(_,_)=>ExitThread());_tray.ContextMenuStrip=menu;
  _tray.DoubleClick+=(_,_)=>Check(true);
  _timer.Tick+=(_,_)=>Check(false);_timer.Start();Check(false);
 }
 private void Check(bool reopen) {
  try {
   var pending=SubmissionOperatorStep.Pending(_directory,_runKey);
   _failed=false;_tray.Text=pending.Count==0?"Crestron: no physical action needed":"Crestron: physical action needed";
   foreach(var handle in pending) {
    if(_windows.TryGetValue(handle.RequestSha256,out var existing)) {if(reopen)existing.Activate();continue;}
    if(!reopen && !_shown.Add(handle.RequestSha256))continue;
    _shown.Add(handle.RequestSha256);
    var window=new OperatorWindow(handle);_windows.Add(handle.RequestSha256,window);
    window.FormClosed+=(_,_)=>_windows.Remove(handle.RequestSha256);window.Show();
   }
  } catch(Exception e) when(e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException) {
   _tray.Text="Crestron: action inbox unavailable";
   if(!_failed) {_tray.ShowBalloonTip(10000,"Action inbox unavailable","Cannot read the private action inbox. Check its connection and records; no test action is implied.",ToolTipIcon.Warning);_failed=true;}
  }
 }
 protected override void Dispose(bool disposing) {
  if(disposing) {
   _timer.Dispose();foreach(var window in _windows.Values.ToArray())window.Dispose();
   _tray.Visible=false;_tray.ContextMenuStrip?.Dispose();_tray.Dispose();
  }
  base.Dispose(disposing);
 }
}
