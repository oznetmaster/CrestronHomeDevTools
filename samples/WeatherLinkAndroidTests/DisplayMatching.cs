// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CrestronHomeNUnit.Android;

namespace WeatherLinkAndroidTests;

public static class DisplayMatching
{
 private static readonly Dictionary<string,(string Title,string Line)> Current=new(StringComparer.Ordinal) {
  ["currentTemperatureDisplay"]=("Temperature","first"),["humiditySummary"]=("Temperature","second"),["pressureSummary"]=("Temperature","third"),
  ["windSummary"]=("Wind","first"),["windDirectionSummary"]=("Wind","second"),["windGustSummary"]=("Wind","third"),
  ["rainRateSummary"]=("Rain","first"),["rainLast24HoursSummary"]=("Rain","second"),["rainChanceSummary"]=("Rain","third"),
  ["sourceSummary"]=("Source","first"),["sourceDetailSummary"]=("Source","second"),["tileStatus"]=("Source","third")};
 public static bool Matches(AndroidHierarchy page,string property,IReadOnlyDictionary<string,string> values) {
  var viewport=page.RequireUnique(CrestronHomePages.Resource("customdevices_componentRecyclerView"));
  string title,line;string? first=null;
  if(Current.TryGetValue(property,out var current)){title=current.Title;line=current.Line;}
  else if(property=="forecastSummary"){title="Forecast";line="first";}
  else if(property.StartsWith("forecastDay",StringComparison.Ordinal)) {
   string stem=property.EndsWith("Title",StringComparison.Ordinal)?property[..^5]:property;
   title=values[stem+"Title"];line=property==stem?"first":"title";
  } else {
   title="-";line=property=="forecastAttributionLine2"?"second":"first";
   first=property=="forecastUpdatedSummary"?values[property]:values["forecastAttributionLine1"];
  }
  string Id(string suffix)=>CrestronHomePages.ResourcePrefix+"customdevice_textdisplay_"+suffix;
  var rows=XDocument.Parse(page.MaskedXml).Descendants("node")
   .Where(n=>(string?)n.Attribute("resource-id")==Id("title")&&(string?)n.Attribute("text")==title)
   .Select(n=>n.Parent!).Where(r=>first==null||r.Elements("node").Any(n=>(string?)n.Attribute("resource-id")==Id("firstlinetext")&&Equal((string?)n.Attribute("text"),first))).ToArray();
  if(rows.Length!=1)return false;
  var nodes=rows[0].Elements("node").Where(n=>(string?)n.Attribute("resource-id")==Id(line=="title"?"title":line+"linetext")).ToArray();
  if(property=="sourceDetailSummary"&&values[property].Length==0) {
   var header=rows[0].Elements("node").Single(n=>(string?)n.Attribute("resource-id")==Id("title"));
   return Visible(header,viewport)&&(nodes.Length==0||nodes.Length==1&&string.IsNullOrEmpty((string?)nodes[0].Attribute("text")));
  }
  return nodes.Length==1&&Equal((string?)nodes[0].Attribute("text"),values[property])&&Visible(nodes[0],viewport);
 }
 private static bool Equal(string? left,string right)=>string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
 private static bool Visible(XElement node,AndroidElement viewport) {
  if((string?)node.Attribute("package")!="com.crestron.phoenix.app"||(string?)node.Attribute("password")=="true"||
   (string?)node.Attribute("visible-to-user")=="false")return false;
  var b=Regex.Match((string?)node.Attribute("bounds")??"",@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
  if(!b.Success)return false;
  int[] p=b.Groups.Cast<Group>().Skip(1).Select(g=>int.TryParse(g.Value,out var i)?i:-1).ToArray();
  return p[0]>=viewport.Left&&p[1]>=viewport.Top&&p[2]<=viewport.Right&&p[3]<=viewport.Bottom&&p[2]>p[0]&&p[3]>p[1];
 }
}
