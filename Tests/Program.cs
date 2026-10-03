using System;
using System.Linq;
using System.Collections.Generic;
using ValorantHelper.Core;

int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
var now=DateTimeOffset.Parse("2026-10-01T00:00:00Z");
var good=new Candidate("ABC123",.96f);
Check(CodeExtractor.Extract("房间码：ABC123，已开").Single()=="ABC123","Chinese surrounding characters");
Check(CodeExtractor.Extract("ABC 123").Single()=="ABC123","OCR space between groups");
Check(!CodeExtractor.Extract("XABC1234 abc123 AbC123 ABCI23 AB0123").Any(),"Reject longer identifiers, lowercase and ambiguous substitutions");
Check(CodeExtractor.Extract("ABC123 ABC123 QWE456").Count()==2,"Unique candidate extraction");
var gate=new RecognitionGate();
Check(gate.Observe(new[]{good},now).State==RecognitionState.Confirming,"First OCR frame waits");
Check(gate.Observe(new[]{good},now.AddMilliseconds(350)).State==RecognitionState.Stable,"Second matching frame confirms");
gate.MarkPublished("ABC123",now.AddMilliseconds(350));
Check(gate.Observe(new[]{good},now.AddMinutes(5)).State==RecognitionState.Confirming,"Long frame gap resets confirmation");
Check(gate.Observe(new[]{good},now.AddMinutes(5).AddMilliseconds(350)).State==RecognitionState.Duplicate,"Persistent same code does not re-copy after cooldown");
gate.ResetPending();
Check(gate.Observe(new[]{good,new Candidate("QWE456",.4f)},now.AddMinutes(6)).State==RecognitionState.Conflict,"Conflicting code blocks automatic copy even if second score low");
Check(gate.Observe(new[]{new Candidate("ABC123",.4f)},now.AddMinutes(6)).State==RecognitionState.LowConfidence,"Low score blocked");
Check(gate.Observe(new[]{new Candidate("ABC123",float.NaN)},now.AddMinutes(6)).State==RecognitionState.LowConfidence,"NaN score blocked");
gate.ResetPending();gate.RequiredFrames=1;
Check(gate.Observe(new[]{new Candidate("QWE456",.97f)},now.AddSeconds(1)).State==RecognitionState.Stable,"New code can publish in fast mode");
gate.MarkPublished("QWE456",now.AddSeconds(1));
Check(gate.Observe(new[]{good},now.AddSeconds(2)).State==RecognitionState.Duplicate,"Recently seen old code blocked");
Check(gate.Observe(new[]{good},now.AddSeconds(62)).State==RecognitionState.Stable,"Old code may return after a different code and cooldown");
gate.ResetPending();gate.RequiredFrames=2;
gate.Observe(new[]{new Candidate("RTY789",.98f)},now);
Check(gate.Observe(Array.Empty<Candidate>(),now.AddMilliseconds(100)).State==RecognitionState.Waiting,"Empty frame clears pending code");
Check(gate.Observe(new[]{new Candidate("RTY789",.98f)},now.AddMilliseconds(200)).State==RecognitionState.Confirming,"Confirmation must be consecutive");
var region=new NormalizedRegion(.1,.2,.3,.1);
Check(region.Map(100,50,1000,600,1200,800)==(200,170,300,60),"ROI maps normalized video coordinates");
Check(region.Map(50,100,500,300,600,500)==(100,160,150,30),"ROI survives resize and letterboxing");
Check(!new NormalizedRegion(double.NaN,0,.3,.1).IsValid&&!new NormalizedRegion(.9,0,.3,.1).IsValid,"Invalid region rejected");
var settings=new AppSettings{IntervalMs=-1,MinimumConfidence=float.NaN,ToggleKey=8,CopyKey=8,Regions=null!,History=new(){new(){Code="abc123"},new(){Code="ABC123"}}};
settings.Sanitize();
Check(settings.IntervalMs==150&&settings.MinimumConfidence==.85f&&settings.ToggleKey!=settings.CopyKey&&settings.History.Count==1&&settings.Regions is not null,"Corrupt settings sanitized");
var full=new NormalizedRegion(0,0,1,1);
Check(full.Map(50,120,900,500,1000,740)==(50,120,900,500),"Full-video recognition excludes letterboxing");
var fillSettings=new AppSettings{AutoFill=true,AutoCopy=false,AlwaysOnTop=true};fillSettings.Sanitize();
Check(fillSettings.AutoCopy&&!fillSettings.AlwaysOnTop,"Auto-fill enables copy and removes helper topmost");
var input=new TestGameInput();
var fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>true);
Check(fill.Sent&&input.Actions=="activate,click,select,paste,","Valid fill clicks field then selects and pastes");
input=new();fill=await new GameFill(input).FillAsync("abc123",()=>true,()=>true);
Check(!fill.Sent&&input.Actions=="","Invalid code sends no input");
input=new(){Error="wrong target"};fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>true);
Check(!fill.Sent&&input.Actions=="","Unbound or changed target sends no input");
input=new(){ModifiersPressed=true};fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>true);
Check(!fill.Sent&&input.Actions=="","Held modifiers block input");
input=new();fill=await new GameFill(input).FillAsync("ABC123",()=>false,()=>true);
Check(!fill.Sent&&input.Actions=="","Canceled publication cannot activate game");
input=new();fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>false);
Check(!fill.Sent&&input.Actions=="","Different clipboard blocks input");
input=new(){LoseFocusAfterClick=true};fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>true);
Check(!fill.Sent&&input.Actions=="activate,click,","Focus change stops before select/paste");
input=new(){ResizeAfterClick=true};fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>true);
Check(!fill.Sent&&input.Actions=="activate,click,","Window resize stops input");
input=new();fill=await new GameFill(input).FillAsync("ABC123",()=>!input.Actions.Contains("click"),()=>true);
Check(!fill.Sent&&!input.Actions.Contains("select"),"Pause during fill prevents subsequent keystrokes");
input=new();fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>!input.Actions.Contains("select"));
Check(!fill.Sent&&!input.Actions.Contains("paste"),"Clipboard changed between select and paste is blocked");
input=new(){ActivationFails=true};fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>true);
Check(!fill.Sent&&input.Actions=="activate,","Denied foreground activation sends no click or key");
input=new(){SelectFails=true};fill=await new GameFill(input).FillAsync("ABC123",()=>true,()=>true);
Check(!fill.Sent&&!input.Actions.Contains("paste"),"Failed select does not continue to paste");
var badTheme=new AppSettings{Theme="Unknown"};badTheme.Sanitize();
Check(badTheme.Theme=="Dark","Unknown theme falls back to dark");
var lightTheme=new AppSettings{Theme="Light"};lightTheme.Sanitize();
Check(lightTheme.Theme=="Light","Light theme survives settings sanitation");
Console.WriteLine($"{passed} checks passed.");

sealed class TestGameInput : IGameInputPlatform
{
    public string Actions="",Error="";
    public bool ModifiersPressed{get;set;}
    public bool IsForeground{get;private set;}=true;
    public bool LoseFocusAfterClick,ResizeAfterClick,ActivationFails,SelectFails;
    public string CheckTarget()=>Error;
    public bool Activate(){Actions+="activate,";return !ActivationFails;}
    public bool ClickInput(){Actions+="click,";if(LoseFocusAfterClick)IsForeground=false;if(ResizeAfterClick)Error="resized";return true;}
    public bool SelectAll(){Actions+="select,";return !SelectFails;}
    public bool Paste(){Actions+="paste,";return true;}
}
