using System.Text.Json;
using System.Windows.Media.Imaging;

namespace ValorantHelper;

public partial class MainWindow
{
    private string testClipboard = "";
    private async Task<bool> WriteCodeAsync(string code,Func<bool> stillCurrent)
    {
        if(testing){if(!stillCurrent())return false;testClipboard=code;return true;}
        return await Services.ClipboardWriter.CopyAsync(code,stillCurrent);
    }
    private async Task WaitUntilAsync(Func<bool> ready,int timeoutMs,string message)
    {
        var until=DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while(!ready()){if(DateTime.UtcNow>until)throw new TimeoutException(message);await Task.Delay(100);}
    }
    private async Task RunSmokeTestAsync()
    {
        var report=new List<string>();
        File.Delete(Path.Combine(storage.Root,"smoke-passed.txt"));
        File.Delete(Path.Combine(storage.Root,"smoke-failed.txt"));
        try
        {
            await WaitUntilAsync(()=>ocrReady&&roomKey.Contains("vrh-demo.local"),90000,"OCR 或本地视频初始化超时");
            VideoState? state=null;
            for(int attempt=0;attempt<100;attempt++){state=await PlayerCall("state");if(state.Ok&&state.Playing)break;await Task.Delay(100);}
            if(state is null||!state.Ok||!state.Playing)throw new Exception("试用视频未播放");
            report.Add("PASS WebView2 initialized and real HTML video is playing");
            if(Services.GameInput.NativeInputSize!=40)throw new Exception("Win64 INPUT 结构大小不正确");
            report.Add("PASS native SendInput structure has correct Win64 size (40 bytes)");
            settings.FullVideo=false;region=null;
            if(await PerformPreflightAsync())throw new Exception("无框选区域的手动模式错误地通过自检");
            report.Add("PASS missing manual ROI is blocked by preflight");
            settings.FullVideo=true;FullVideoCheck.IsChecked=true;
            settings.AutoFill=true;AutoFillCheck.IsChecked=true;
            report.Add("NOTE auto-fill uses an isolated fake game input platform; no real game receives input");
            string originalBoundsJson=await Browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify((()=>{const r=document.querySelector('video').getBoundingClientRect();return {x:r.left,y:r.top,w:r.width,h:r.height};})())");
            string originalBounds=JsonSerializer.Deserialize<string>(originalBoundsJson)!;
            var focused=await EnterPureAsync()??throw new Exception("纯画面切换失败");
            await Task.Delay(200);
            string unchanged=await Browser.CoreWebView2.ExecuteScriptAsync($"(()=>{{const old={originalBounds},r=document.querySelector('video').getBoundingClientRect();return Math.abs(old.x-r.left)<2&&Math.abs(old.y-r.top)<2&&Math.abs(old.w-r.width)<2&&Math.abs(old.h-r.height)<2&&!document.fullscreenElement;}})()");
            if(unchanged!="true"||NavigationBar.Visibility!=Visibility.Visible)throw new Exception("开始识别自动放大了视频或进入全屏");
            report.Add("PASS starting recognition preserves original video rectangle and does not enter fullscreen");
            string hidden=await Browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({chat:getComputedStyle(document.querySelector('aside')).visibility, header:getComputedStyle(document.querySelector('header')).visibility, video:getComputedStyle(document.querySelector('video')).visibility})");
            string text=JsonSerializer.Deserialize<string>(hidden)??"";
            if(!text.Contains("\"chat\":\"hidden\"")||!text.Contains("\"header\":\"hidden\"")||!text.Contains("\"video\":\"visible\""))throw new Exception("聊天/网页装饰未隐藏");
            report.Add("PASS chat/header hidden, only video visible");
            await Browser.CoreWebView2.ExecuteScriptAsync("(()=>{const gift=document.createElement('div');gift.id='gift-overlay-test';gift.textContent='GIFT_OVERLAY_TEST';gift.style.cssText='position:fixed;bottom:0;left:0;width:400px;height:90px;background:red;visibility:visible!important;z-index:2147483647';document.body.appendChild(gift);})()");
            await Task.Delay(100);
            string giftDisplay=await Browser.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.getElementById('gift-overlay-test')).display");
            if(!giftDisplay.Contains("none"))throw new Exception("动态礼物浮层未隐藏");
            report.Add("PASS dynamic gift overlay with visibility:visible!important is suppressed");
            await Browser.CoreWebView2.ExecuteScriptAsync("window.setDemoCode('ABC123')");
            await StartRecognitionAsync();
            await WaitUntilAsync(()=>currentCode=="ABC123"&&testClipboard=="ABC123"&&((FakeGameInput)inputPlatform).Pastes==1,30000,"ABC123 未被识别："+RecognitionHint.Text);
            if(RegionPreview.Source is not BitmapSource preview||preview.PixelWidth<250)throw new Exception("全画面预览未刷新");
            if(!PreflightStatus.Text.StartsWith("✓"))throw new Exception("自检未通过");
            report.Add("PASS no ROI selection -> preflight -> full-video preview -> PaddleOCR -> stable ABC123 -> isolated copy and auto-fill");
            int oldCount=history.Count;await Task.Delay(2500);
            if(history.Count!=oldCount||((FakeGameInput)inputPlatform).Pastes!=1)throw new Exception("同一码被重复记录或填入");
            report.Add("PASS repeated frames do not duplicate code/history");
            await Browser.CoreWebView2.ExecuteScriptAsync("window.setDemoCode('QWE456')");
            await WaitUntilAsync(()=>currentCode=="QWE456"&&testClipboard=="QWE456"&&((FakeGameInput)inputPlatform).Pastes==2,30000,"新码 QWE456 未被识别");
            report.Add("PASS new code replaces previous result");
            StopRecognition("自动测试已暂停识别");
            await pageRestore;
            string pauseLayout=await Browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({pure:window.__vrhPlayer.state().pure,chat:getComputedStyle(document.querySelector('aside')).visibility,header:getComputedStyle(document.querySelector('header')).visibility})");
            string pauseText=JsonSerializer.Deserialize<string>(pauseLayout)!;
            if(!pauseText.Contains("\"pure\":false")||!pauseText.Contains("\"chat\":\"visible\"")||!pauseText.Contains("\"header\":\"visible\""))throw new Exception("暂停未自动恢复网页聊天");
            report.Add("PASS pause automatically restores webpage, chat and header");
            int pausedCount=history.Count;await Browser.CoreWebView2.ExecuteScriptAsync("window.setDemoCode('RTY789')");await Task.Delay(1200);
            if(history.Count!=pausedCount||currentCode!="QWE456"||((FakeGameInput)inputPlatform).Pastes!=2)throw new Exception("暂停后仍发布结果或填入");
            report.Add("PASS pause cancels publication of subsequent frames");
            Width=1150;Height=780;UpdateLayout();await Task.Delay(300);
            settings.FullVideo=false;FullVideoCheck.IsChecked=false;
            region=new NormalizedRegion(.052,.13,.32,.102);settings.Regions[roomKey]=region;
            await StartRecognitionAsync();await WaitUntilAsync(()=>currentCode=="RTY789"&&((FakeGameInput)inputPlatform).Pastes==3,30000,"窗口缩放后识别区域或填入失效");
            report.Add("PASS normalized video ROI survives window resize");
            StopRecognition("本地自动测试全部通过");
            Width=1360;Height=920;UpdateLayout();await Task.Delay(200);
            await PreflightForFullSnapshotAsync();
            await VerifyAppearanceAsync(report);
            await SaveUiSnapshotAsync(Path.Combine(storage.Root,"ui-smoke.png"));
            await pageRestore;
            string restored=await Browser.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('aside')).visibility");
            if(!restored.Contains("visible"))throw new Exception("返回网页未恢复聊天布局");
            report.Add("PASS return-to-page restores original layout");
            report.Add("NOTE clipboard was isolated in memory; no user clipboard was changed by automated tests");
            File.WriteAllLines(Path.Combine(storage.Root,"smoke-passed.txt"),report);
        }
        catch(Exception ex){storage.Log("Smoke test failed",ex);File.WriteAllText(Path.Combine(storage.Root,"smoke-failed.txt"),string.Join("\n",report)+"\n"+ex);}
        finally{Close();}
    }
    private async Task PreflightForFullSnapshotAsync()
    {
        settings.FullVideo=true;FullVideoCheck.IsChecked=true;settings.AutoFill=false;AutoFillCheck.IsChecked=false;
        GameTargetLabel.Text="打开游戏加入房间窗口后绑定位置";
        CopyStatus.Text="本地试用 · 已识别";CopyStatus.SetResourceReference(TextBlock.ForegroundProperty,"Success");
        await PerformPreflightAsync();StopRecognition("已暂停，网页弹幕和聊天已恢复");await pageRestore;RefreshControls();
    }
    private async Task SaveUiSnapshotAsync(string file)
    {
        using var stream=new MemoryStream();
        await Browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,stream);
        stream.Position=0;var screenshot=new BitmapImage();screenshot.BeginInit();screenshot.CacheOption=BitmapCacheOption.OnLoad;screenshot.StreamSource=stream;screenshot.EndInit();screenshot.Freeze();
        SelectionImage.Source=screenshot;SelectionCanvas.Visibility=Visibility.Collapsed;SelectionInstruction.Visibility=Visibility.Collapsed;
        Browser.Visibility=Visibility.Hidden;SelectionLayer.Visibility=Visibility.Visible;UpdateLayout();
        await Task.Delay(100);
        var target=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);
        target.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));
        using(var output=File.Create(file))encoder.Save(output);
        SelectionLayer.Visibility=Visibility.Collapsed;Browser.Visibility=Visibility.Visible;SelectionImage.Source=null;
        SelectionCanvas.Visibility=Visibility.Visible;SelectionInstruction.Visibility=Visibility.Visible;
    }
}
