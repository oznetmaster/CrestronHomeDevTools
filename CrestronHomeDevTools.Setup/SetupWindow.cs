// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;

namespace CrestronHomeDevTools.Setup;

internal sealed class SetupWindow : Form
{
 private readonly string _directory;
 private readonly Label _status = new () { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding (12), Text = "Enter details once, edit them when needed, and reuse them for subsequent submissions." };
 private readonly List<Func<bool>> _dirtyChecks = [];
 private DevToolsPrivateStore Store () => File.Exists (Path.Combine (_directory, "store.json")) ? DevToolsPrivateStore.Open (_directory) : DevToolsPrivateStore.Create (_directory);
 private bool HasStore => File.Exists (Path.Combine (_directory, "store.json"));
 public SetupWindow (string directory)
 {
  _directory = directory;
  Text = "Crestron Home — Submission setup";
  Size = new Size (1000, 800); MinimumSize = new Size (760, 550); StartPosition = FormStartPosition.CenterScreen;
  Font = new Font ("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
  var heading = new Label { Dock = DockStyle.Top, Height = 74, Padding = new Padding (16), Text = "Submission setup\nPrivate inputs are encrypted for this Windows account. Saving is not signing or sending.", BackColor = Color.FromArgb (230, 240, 235) };
  var tabs = new TabControl { Dock = DockStyle.Fill };
  tabs.TabPages.Add (ProfilePage<SubmissionDeveloperProfile> ("Developer", "Shared contact, support, signing and delivery details."));
  tabs.TabPages.Add (ProfilePage<SubmissionDriverProfile> ("Driver", "Reusable product facts and test restrictions. Empty support overrides inherit developer defaults."));
  tabs.TabPages.Add (ProfilePage<SubmissionRunProfile> ("Submission", "Version and equipment for this attempt. Save these details before checking readiness."));
  tabs.TabPages.Add (CredentialsPage ());
  tabs.TabPages.Add (ReadinessPage ());
  Controls.Add (tabs); Controls.Add (heading); Controls.Add (_status);
  FormClosing += (_, e) => { if (_dirtyChecks.Any (f => f ()) && MessageBox.Show (this, "Some edits have not been saved. Close and discard those edits?", "Unsaved edits", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true; };
 }
 private void Safe (Action action)
 {
  try { action (); }
  catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException or System.Text.Json.JsonException or System.Security.SecurityException or FormatException or OverflowException)
  {
   // Never display exception payloads which might contain private input values.
   _status.Text = "Operation not completed. Check the selected profile, store access and fields. Reload before retrying a conflicting edit.";
   MessageBox.Show (this, _status.Text, "Setup needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning);
  }
 }
 private static Button Button (string text, Action action)
 {
  var button = new Button { Text = text, AutoSize = true, Padding = new Padding (7, 3, 7, 3) }; button.Click += (_, _) => action (); return button;
 }
 private TabPage ProfilePage<T> (string title, string description) where T : class, new ()
 {
  var page = new TabPage (title) { Padding = new Padding (12) };
  var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
  var name = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDown, AccessibleName = title + " profile name" };
  int revision = 0; string? loadedName = null; bool dirty = false, binding = false;
  T value = new ();
  _dirtyChecks.Add (() => dirty);
  var entries = new Dictionary<PropertyInfo, TextBox> ();
  var table = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding (8) };
  table.ColumnStyles.Add (new ColumnStyle (SizeType.Percent, 33)); table.ColumnStyles.Add (new ColumnStyle (SizeType.Percent, 67));
  string? category = null;
  var help = new ToolTip { AutoPopDelay = 20000 };
  page.Disposed += (_, _) => help.Dispose ();
  foreach (var property in typeof (T).GetProperties ())
  {
   string nextCategory = property.GetCustomAttribute<CategoryAttribute> ()?.Category ?? "Details";
   if (nextCategory != category)
   {
    category = nextCategory;
    var label = new Label { Text = category, AutoSize = true, Font = new Font (Font, FontStyle.Bold), Margin = new Padding (0, 18, 0, 10) };
    table.Controls.Add (label, 0, table.RowCount++); table.SetColumnSpan (label, 2);
   }
   bool multiline = property.Name is "PostalAddress" or "Description" or "Installation" or "Requirements" or "ReleaseNotes" or "Configuration" or "Usage" or "Limitations" or "Troubleshooting" or "Licensing" or "Models" or "TestDeviceModels" or "RealUseRestrictions" or "PermittedTestChanges" or "UnavailableTests" or "AdditionalInformation" or "SubmissionNotes";
   string labelText = property.GetCustomAttribute<DisplayNameAttribute> ()?.DisplayName ?? property.Name;
   if (property.IsDefined (typeof (System.ComponentModel.DataAnnotations.RequiredAttribute))) labelText += " *";
   var field = new TextBox { Dock = DockStyle.Top, Multiline = multiline, Height = multiline ? 90 : 30, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, AccessibleName = labelText, Margin = new Padding (0, 0, 0, 12) };
   help.SetToolTip (field, property.GetCustomAttribute<DescriptionAttribute> ()?.Description ?? "");
   field.TextChanged += (_, _) => { if (!binding) { property.SetValue (value, field.Text); dirty = true; } };
   entries.Add (property, field);
   table.Controls.Add (new Label { Text = labelText, AutoSize = true, Margin = new Padding (0, 4, 12, 12) }, 0, table.RowCount);
   table.Controls.Add (field, 1, table.RowCount++);
  }
  void Bind () { binding = true; foreach (var pair in entries) pair.Value.Text = (string?)pair.Key.GetValue (value) ?? ""; binding = false; dirty = false; }
  void RefreshNames () { string text = name.Text; name.Items.Clear (); if (HasStore) name.Items.AddRange (Store ().ListSetupProfiles<T> ().Cast<object> ().ToArray ()); name.Text = text; }
  bool Discard () => !dirty || MessageBox.Show (this, "Discard unsaved edits to this profile?", "Unsaved edits", MessageBoxButtons.YesNo) == DialogResult.Yes;
  toolbar.Controls.Add (new Label { Text = "Profile name", AutoSize = true, Margin = new Padding (0, 10, 8, 0) }); toolbar.Controls.Add (name);
  toolbar.Controls.Add (Button ("New", () => { if (!Discard ()) return; value = new (); revision = 0; loadedName = null; name.Text = ""; Bind (); }));
  toolbar.Controls.Add (Button ("Load / reload", () => Safe (() => { if (!Discard ()) return; var saved = Store ().LoadSetupProfile<T> (name.Text.Trim ()); value = saved.Value; revision = saved.Revision; loadedName = saved.Name; Bind (); _status.Text = "Profile loaded. You can edit it and save a new revision."; })));
  toolbar.Controls.Add (Button ("Save", () => Safe (() => {
   string selected = name.Text.Trim ();
   var saved = Store ().SaveSetupProfile (selected, value, selected == loadedName ? revision : 0);
   revision = saved.Revision; loadedName = saved.Name; dirty = false; RefreshNames ();
   _status.Text = $"Saved encrypted {title.ToLowerInvariant ()} profile, revision {revision}. Incomplete drafts can be resumed later.";
  })));
  toolbar.Controls.Add (Button ("Check fields", () => {
   var errors = SubmissionSetupValidation.Check (value);
   MessageBox.Show (this, errors.Count == 0 ? "These fields are complete. Use Readiness to check the combined profiles and saved inputs." : string.Join (Environment.NewLine, errors), "Input check");
  }));
  name.DropDown += (_, _) => Safe (RefreshNames);
  var intro = new Label { Dock = DockStyle.Top, Height = 74, Text = description + "\n* Required for a ready snapshot; incomplete drafts can still be saved.\nProfile names use letters, numbers, hyphens or underscores. Hover over a field for help." };
  var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add (table);
  page.Controls.Add (scroll); page.Controls.Add (toolbar); page.Controls.Add (intro);
  Bind (); Safe (RefreshNames); return page;
 }
 private TabPage CredentialsPage ()
 {
  var page = new TabPage ("Credentials & signature") { Padding = new Padding (20), AutoScroll = true };
  var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
  table.ColumnStyles.Add (new ColumnStyle (SizeType.Absolute, 235)); table.ColumnStyles.Add (new ColumnStyle (SizeType.Percent, 100));
  TextBox Field (string label, bool secret = false) { var text = new TextBox { Width = 430, UseSystemPasswordChar = secret, AccessibleName = label, Margin = new Padding (0, 0, 0, 12) }; table.Controls.Add (new Label { Text = label, AutoSize = true }, 0, table.RowCount); table.Controls.Add (text, 1, table.RowCount++); return text; }
  var explanation = new Label { Text = "Use the saved entry name in your profiles. Passwords and signatures remain encrypted.\nSaving here does not connect to a processor, send email or apply a signature.", AutoSize = true, MaximumSize = new Size (760, 0), Margin = new Padding (0, 0, 0, 25) };
  table.Controls.Add (explanation, 0, table.RowCount++); table.SetColumnSpan (explanation, 2);
  var name = Field ("Saved entry name");
  var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 }; kind.Items.AddRange (Enum.GetNames<DevToolsCredentialPurpose> ()); kind.SelectedItem = "Smtp";
  table.Controls.Add (new Label { Text = "Credential purpose", AutoSize = true }, 0, table.RowCount); table.Controls.Add (kind, 1, table.RowCount++);
  var host = Field ("Endpoint hostname / IP"); var user = Field ("Username"); var password = Field ("Password", true);
  var port = Field ("Port (if applicable)"); var sender = Field ("Sender email (SMTP only)"); var certificate = Field ("Verified HTTPS certificate SHA-256"); var ssh = Field ("Verified SSH fingerprint");
  bool credentialDirty = false;
  _dirtyChecks.Add (() => credentialDirty);
  foreach (var field in new[] { name, host, user, password, port, sender, certificate, ssh })
   field.TextChanged += (_, _) => credentialDirty = true;
  kind.SelectedIndexChanged += (_, _) => credentialDirty = true;
  var buttons = new FlowLayoutPanel { AutoSize = true };
  buttons.Controls.Add (Button ("Show saved entry names", () => Safe (() => MessageBox.Show (this, HasStore ? string.Join (Environment.NewLine, Store ().ListNames ()) : "No saved inputs yet.", "Saved inputs"))));
  buttons.Controls.Add (Button ("Load selected credential", () => Safe (() => {
   if (credentialDirty && MessageBox.Show (this, "Discard unsaved credential edits and load the selected entry?", "Unsaved edits", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
   var saved = Store ().LoadCredential (name.Text.Trim (), Enum.Parse<DevToolsCredentialPurpose> (kind.Text), host.Text.Trim ());
   user.Text = saved.UserName; password.Text = saved.Password; port.Text = saved.Port?.ToString () ?? ""; sender.Text = saved.Sender ?? ""; certificate.Text = saved.CertificateSha256 ?? ""; ssh.Text = saved.SshFingerprint ?? "";
   credentialDirty = false; _status.Text = "Credential loaded for editing; password remains masked.";
  })));
  buttons.Controls.Add (Button ("Save credential", () => Safe (() => {
   var store = Store (); bool replace = store.ListNames ().Contains (name.Text.Trim ());
   if (replace && MessageBox.Show (this, "Replace this named saved input? Existing workflows using that name will use the new credential.", "Replace saved input", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
   store.SaveCredential (name.Text.Trim (), new (Enum.Parse<DevToolsCredentialPurpose> (kind.Text), host.Text.Trim (), user.Text.Trim (), password.Text, string.IsNullOrWhiteSpace (port.Text) ? null : int.Parse (port.Text), Empty (sender.Text), Empty (certificate.Text), Empty (ssh.Text)), replace);
   password.Clear (); credentialDirty = false; _status.Text = "Credential saved encrypted. Password removed from the editor.";
  })));
  table.Controls.Add (buttons, 0, table.RowCount++); table.SetColumnSpan (buttons, 2);
  var imageName = Field ("Signature entry name");
  var image = Button ("Choose and save signature image…", () => Safe (() => {
   using var dialog = new OpenFileDialog { Title = "Choose a signature image to store encrypted", Filter = "Signature image (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png" };
   if (dialog.ShowDialog (this) != DialogResult.OK) return;
   if (new FileInfo (dialog.FileName).Length > 8388608) throw new ArgumentException ("Image too large.");
   var store = Store (); bool replace = store.ListNames ().Contains (imageName.Text.Trim ());
   if (replace && MessageBox.Show (this, "Replace this saved signature entry? No document will be signed.", "Replace signature", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
   byte[] bytes = File.ReadAllBytes (dialog.FileName);
   try { store.SaveSignature (imageName.Text.Trim (), bytes, Path.GetExtension (dialog.FileName), replace); }
   finally { CryptographicOperations.ZeroMemory (bytes); }
   _status.Text = "Signature stored encrypted. Exact document approval is still required before signing.";
  }));
  table.Controls.Add (image, 1, table.RowCount++); page.Controls.Add (table); return page;
 }
 private static string? Empty (string text) => string.IsNullOrWhiteSpace (text) ? null : text.Trim ();
 private TabPage ReadinessPage ()
 {
  var page = new TabPage ("Readiness & snapshots") { Padding = new Padding (20) };
  var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
  var run = new TextBox { Width = 200, PlaceholderText = "Saved submission profile", AccessibleName = "Submission profile to check" };
  var snapshot = new TextBox { Width = 200, PlaceholderText = "New snapshot name", AccessibleName = "New snapshot name" };
  var results = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = "Save your Developer, Driver and Submission profiles first.\r\n\r\nThis checks reusable input data, not test completion or Crestron acceptance. Snapshots retain the chosen revisions; later profile edits do not change existing snapshots.\r\n\r\nThe workflow reads saved profiles through the public DevTools APIs. It still collects actual evidence and obtains exact signing/delivery authorization." };
  top.Controls.Add (run); top.Controls.Add (Button ("Check all inputs", () => Safe (() => {
   var errors = Store ().CheckSubmissionSetup (run.Text.Trim ());
   results.Text = errors.Count == 0 ? "Saved inputs are ready. No external operation has been performed. You may create a snapshot for the workflow." : string.Join (Environment.NewLine, errors);
  })));
  top.Controls.Add (snapshot); top.Controls.Add (Button ("Create snapshot", () => Safe (() => {
   _ = Store ().CreateSubmissionSetupSnapshot (run.Text.Trim (), snapshot.Text.Trim ());
   results.Text = "Encrypted snapshot saved. Existing snapshots cannot be overwritten.\r\n\r\nExisting commands accept this file with --credentials:\r\n" + Store ().GetSubmissionSetupSnapshotPath (snapshot.Text.Trim ()) + "\r\n\r\nPrepare review drafts to reuse the saved help content and operation defaults.";
  })));
  top.Controls.Add (Button ("Prepare review drafts", () => Safe (() => {
   var prepared = Store ().PrepareSubmissionSetupInputs (snapshot.Text.Trim ());
   results.Text = "Prepared inside the protected store:\r\n\r\nHelp content:\r\n" + prepared.HelpContentPath + "\r\n\r\nRelease notes:\r\n" + prepared.ReleaseNotesPath + "\r\n\r\nForm and delivery defaults:\r\n" + prepared.OperationDefaultsPath + "\r\n\r\nEncrypted credentials input:\r\n" + prepared.CredentialsPath + "\r\n\r\nReview the drafts before using them. No signature applied, network connection, upload or email performed.";
  })));
  top.Controls.Add (Button ("Prepare rehearsal profile", () => Safe (() => {
   var prepared = Automation.SubmissionAutomationSetup.PrepareRehearsal (Store (), snapshot.Text.Trim ());
   results.Text = "Prepared a fresh private rehearsal profile. No tests or provider operations started.\r\n\r\nRelease profiles:\r\n" + prepared.ProfilesPath + "\r\n\r\nEmpty run registry:\r\n" + prepared.RegistryPath + "\r\n\r\nSetup provenance:\r\n" + prepared.ProvenancePath + "\r\n\r\n" +
    (prepared.Configuration.AllStageBindingsPresent ? "All stage bindings are present; execution and evidence are not yet validated." : "Complete these bindings before a full rehearsal:\r\n" + string.Join ("\r\n", prepared.Configuration.MissingBindings)) + "\r\n\r\nUse the public automation worker with these paths. Provision selected worker credentials separately; do not share this entire setup store.";
  })));
  page.Controls.Add (results); page.Controls.Add (top); return page;
 }
}
