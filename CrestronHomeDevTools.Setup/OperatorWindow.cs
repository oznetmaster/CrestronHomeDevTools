// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;

namespace CrestronHomeDevTools.Setup;

/// <summary>A planned physical action, distinct from signing and delivery approval.</summary>
internal sealed class OperatorWindow:Form
{
 private readonly SubmissionOperatorHandle _handle;
 private readonly TextBox _instructions=new() {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};
 private readonly Label _status=new() {Dock=DockStyle.Bottom,Height=70};
 private readonly Button _done=new() {Text="Done",AutoSize=true};
 private readonly Button _unable=new() {Text="Unable to do this",AutoSize=true};
 private readonly System.Windows.Forms.Timer _refresh=new() {Interval=1000};
 public OperatorWindow(SubmissionOperatorHandle handle)
 {
  _handle=handle;
  Text="Crestron submission — physical action needed";
  Size=new(800,550);MinimumSize=new(650,450);Padding=new(16);Font=new("Segoe UI",11);
  var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=55};
  buttons.Controls.Add(_done);buttons.Controls.Add(_unable);
  Controls.Add(_instructions);Controls.Add(buttons);Controls.Add(_status);
  _done.Click+=(_,_)=>Respond(SubmissionOperatorOutcome.Done);
  _unable.Click+=(_,_)=>Respond(SubmissionOperatorOutcome.Unable);
  _refresh.Tick+=(_,_)=>RefreshRequest();
  Shown+=(_,_)=>{RefreshRequest();_refresh.Start();};
  FormClosed+=(_,_)=>_refresh.Dispose();
 }
 private void Respond(SubmissionOperatorOutcome outcome)
 {
  try {_=SubmissionOperatorStep.Respond(_handle,outcome);RefreshRequest();}
  catch(Exception e) when(IsRequestError(e)) {ShowError();}
 }
 private void RefreshRequest()
 {
  try {
   var status=SubmissionOperatorStep.Read(_handle);
   string text=$"Target: {status.Request.Target}\r\nStep: {status.Request.Step}\r\nRun: {status.Request.RunKey}\r\n\r\n{status.Request.Instructions}\r\n\r\nRequested: {SubmissionDisplayTime.Local(status.Request.CreatedUtc)}\r\nExpires: {SubmissionDisplayTime.Local(status.Request.ExpiresUtc)}\r\n\r\nDone records your action only. The test checks its effect separately. Closing this window leaves the request pending. This is not approval to sign or send a submission.";
   if(_instructions.Text!=text)_instructions.Text=text;
   _done.Enabled=_unable.Enabled=status.Waiting && DateTimeOffset.UtcNow<status.Request.ExpiresUtc;
   _status.Text=status.Response!=null?$"Recorded: {status.Response.Outcome} at {SubmissionDisplayTime.Local(status.Response.RecordedUtc)}":
    _done.Enabled?"Waiting for your action. Read the target and instructions before responding.":"This request expired. Do not perform the action. Check the workflow for any restoration instructions.";
  } catch(Exception e) when(IsRequestError(e)) {ShowError();}
 }
 private static bool IsRequestError(Exception e)=>e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException;
 private void ShowError() {
  _done.Enabled=_unable.Enabled=false;
  _status.Text="Request unavailable, changed, or already answered. No action was authorized by this error. Reload the current request before continuing.";
 }
}
