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
 private readonly Button _unable=new() {Text="Cannot perform this action",AutoSize=true};
 private readonly Button _later=new() {Text="Do this later",AutoSize=true};
 private readonly TextBox _reason=new() {Multiline=true,Dock=DockStyle.Bottom,Height=75,MaxLength=2048,PlaceholderText="Reason this action cannot be performed (required for Cannot perform)"};
 private readonly System.Windows.Forms.Timer _refresh=new() {Interval=1000};
 public OperatorWindow(SubmissionOperatorHandle handle)
 {
  _handle=handle;
  Text="Crestron submission — physical action needed";
  Size=new(800,650);MinimumSize=new(750,550);Padding=new(16);Font=new("Segoe UI",11);
  StartPosition=FormStartPosition.CenterScreen;
  var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=55};
  buttons.Controls.Add(_done);buttons.Controls.Add(_later);buttons.Controls.Add(_unable);
  Controls.Add(_instructions);Controls.Add(_reason);Controls.Add(buttons);Controls.Add(_status);
  _done.Click+=(_,_)=>Respond(SubmissionOperatorOutcome.Done);
  _unable.Click+=(_,_)=>Respond(SubmissionOperatorOutcome.Unable);
  _later.Click+=(_,_)=>Close();
  _refresh.Tick+=(_,_)=>RefreshRequest();
  Shown+=(_,_)=>{RefreshRequest();TopMost=true;Activate();_refresh.Start();};
  FormClosed+=(_,_)=>_refresh.Dispose();
 }
 private void Respond(SubmissionOperatorOutcome outcome)
 {
  if(outcome==SubmissionOperatorOutcome.Unable && string.IsNullOrWhiteSpace(_reason.Text)) {
   _status.Text="Please explain why this action cannot be performed. The workflow will pause.";_reason.Focus();return;
  }
  try {_=SubmissionOperatorStep.Respond(_handle,outcome,outcome==SubmissionOperatorOutcome.Unable?_reason.Text.Trim():null);RefreshRequest();}
  catch(Exception e) when(IsRequestError(e)) {ShowError();}
 }
 private void RefreshRequest()
 {
  try {
   var status=SubmissionOperatorStep.Read(_handle);
   bool readiness=status.Request.IsReadiness;
   Text=readiness?"Crestron submission — ready for a physical test?":"Crestron submission — physical action needed";
   _done.Text=readiness?"I'm ready":"Done";_later.Visible=readiness;
   string timing=readiness?"No expiry. You may return tomorrow or later.":$"Expires: {SubmissionDisplayTime.Local(status.Request.ExpiresUtc)}";
   string meaning=readiness?"Do not operate the device yet. I'm ready asks the worker to prepare recording; wait for the separate action prompt.":"Done records your action only. The test checks its effect separately.";
   string text=$"{status.Request.Instructions}\r\n\r\nTarget: {status.Request.Target}\r\nRequested: {SubmissionDisplayTime.Local(status.Request.CreatedUtc)}\r\n{timing}\r\n\r\n{meaning}\r\nClosing this window leaves the request pending. Reopen it from Crestron submission actions in the notification area. This is not approval to sign or send a submission.\r\n\r\nStep: {status.Request.Step}\r\nRun: {status.Request.RunKey}";
   if(_instructions.Text!=text)_instructions.Text=text;
   _done.Enabled=_unable.Enabled=_later.Enabled=status.Waiting && !status.Request.IsExpired(DateTimeOffset.UtcNow);
   _status.Text=status.Response!=null?$"Recorded: {status.Response.Outcome} at {SubmissionDisplayTime.Local(status.Response.RecordedUtc)} {status.Response.Reason}":
    _done.Enabled?(readiness?"Waiting for you. The action timer has not started.":"Recording is ready. Perform only the action above, then choose Done."):"This request expired. Do not perform the action. Check the workflow for any restoration instructions.";
  } catch(Exception e) when(IsRequestError(e)) {ShowError();}
 }
 private static bool IsRequestError(Exception e)=>e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException;
 private void ShowError() {
  _done.Enabled=_unable.Enabled=false;
  _status.Text="Request unavailable, changed, or already answered. No action was authorized by this error. Reload the current request before continuing.";
 }
}
