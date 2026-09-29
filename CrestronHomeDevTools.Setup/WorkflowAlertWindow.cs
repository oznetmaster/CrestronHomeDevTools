// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using CrestronHomeDevTools.Automation;
namespace CrestronHomeDevTools.Setup;

internal sealed class WorkflowAlertWindow:Form
{
 public WorkflowAlertWindow(SubmissionOperatorAlert alert) {
  bool completed=alert.State=="Completed",review=alert.State=="NeedsInput" && alert.Reason=="rehearsal-ready-for-review";
  Text=completed?"Crestron submission workflow completed":review?"Crestron rehearsal ready for review":"Crestron submission needs attention";
  Width=650;Height=350;StartPosition=FormStartPosition.CenterScreen;
  MinimumSize=new(500,280);ShowInTaskbar=true;
  Shown+=(_,_)=>{TopMost=true;Activate();};
  var message=new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=SystemColors.Control};
  message.Text=$"{alert.Repository} — {alert.Tag}{Environment.NewLine}"+
   $"Profile: {alert.Profile}{Environment.NewLine}"+
   $"Status changed: {SubmissionDisplayTime.Local(alert.ChangedUtc)}{Environment.NewLine}"+
   $"Stage: {alert.Stage}{Environment.NewLine}State: {alert.State}{Environment.NewLine}"+
   $"Reason: {alert.Reason}{Environment.NewLine}{Environment.NewLine}"+
   (alert.Reason=="installed-driver-readiness-failed-before-tests"?"The driver or one of its child devices was not ready. The physical test did not start, so do not operate the device. Inspect the retained driver readiness report; your readiness response remains recorded.":
    alert.Reason=="android-readiness-failed-before-tests"?"The app screen was not ready for recording. The physical test did not start, so do not operate the device. Inspect the retained screen and readiness report; your readiness response remains recorded.":
    completed?"The workflow reached final retention. This notice does not establish certification or portal publication.":
    review?"The rehearsal reached unsigned review. Open the retained documents and evidence. No signing or delivery was performed.":
    "Open the retained evidence to inspect the original error. No automatic retry or reset has been requested.")+
   $"{Environment.NewLine}{Environment.NewLine}Closing this notice only dismisses it. It does not approve a test, signature, upload or email.";
  var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft};
  var close=new Button {Text="Close notice",AutoSize=true};close.Click+=(_,_)=>Close();
  var open=new Button {Text="Open evidence",AutoSize=true};
  open.Click+=(_,_)=>{
   try {var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};info.ArgumentList.Add(alert.EvidenceDirectory);Process.Start(info)?.Dispose();}
   catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException){MessageBox.Show(this,"The evidence folder could not be opened. Check the worker connection.","Evidence unavailable");}
  };
  buttons.Controls.Add(close);buttons.Controls.Add(open);Controls.Add(message);Controls.Add(buttons);Padding=new Padding(16);
 }
}
