// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using CrestronHomeNUnit.Android;

namespace WeatherLinkAndroidTests;

// The published high-level nested-page helper supports Room extensions. WeatherLink
// is Home-only, so use the public transport with the same guarded, observed bounds.
// No input is retried and a pending transition must resolve before cleanup sends one.
public sealed class ObservedNavigation(AndroidDevice device,IAndroidCommandTransport transport,Action active)
{
 private Action<AndroidHierarchy>? pendingBefore,pendingAfter;
 public async Task WaitAsync(Action<AndroidHierarchy> guard,CancellationToken token) {
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(30));
  while(true) {
   active();
   try {guard(await device.CaptureAsync(deadline.Token));return;}
   catch(InvalidOperationException) {await Task.Delay(250,deadline.Token);}
  }
 }
 public async Task ResolveAsync(CancellationToken token) {
  if(pendingAfter==null)return;
  var before=pendingBefore!;var after=pendingAfter;
  await WaitAsync(h=> {
   after(h);
   try {before(h);}
   catch(InvalidOperationException) {return;}
   throw new InvalidOperationException("Previous page still visible; the input will not be repeated.");
  },token);
  pendingBefore=null;pendingAfter=null;
 }
 public async Task TapAsync(Func<AndroidHierarchy,AndroidElement> select,Action<AndroidHierarchy> before,
  Action<AndroidHierarchy> after,CancellationToken token) {
  await ResolveAsync(token);
  active();var h=await device.CaptureAsync(token);before(h);var element=select(h);
  if(!element.Enabled)throw new InvalidOperationException("Observed control is disabled.");
  token.ThrowIfCancellationRequested();active();pendingBefore=before;pendingAfter=after;
  await transport.ExecuteAsync(["shell","input","tap",Number((element.Left+element.Right)/2),Number((element.Top+element.Bottom)/2)],token);
  await ResolveAsync(token);
 }
 public async Task ScrollAsync(Func<AndroidHierarchy,AndroidElement> select,Action<AndroidHierarchy> guard,CancellationToken token) {
  await ResolveAsync(token);active();var h=await device.CaptureAsync(token);guard(h);var e=select(h);
  if(!e.Enabled||e.Bottom-e.Top<80)throw new InvalidOperationException("Scroll container unavailable.");
  token.ThrowIfCancellationRequested();active();
  await transport.ExecuteAsync(["shell","input","swipe",Number((e.Left+e.Right)/2),Number(e.Top+(e.Bottom-e.Top)*3/4),
   Number((e.Left+e.Right)/2),Number(e.Top+(e.Bottom-e.Top)/4),"350"],token);
  await WaitAsync(guard,token);
 }
 private static string Number(int value)=>value.ToString(CultureInfo.InvariantCulture);
}
