using System.Text.Json;

namespace ValorantHelper;

public partial class MainWindow
{
    private async Task RunDouyinCheckAsync()
    {
        var report=new List<string>();
        try
        {
            await WaitUntilAsync(()=>roomKey.Contains("live.douyin.com"),45000,"抖音首页加载超时");
            await Task.Delay(5000);
            report.Add("PASS Douyin page loaded in embedded WebView2");
            string title=await Browser.CoreWebView2.ExecuteScriptAsync("document.title");
            report.Add("Page title: "+title);
            string link=await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('a[href]')].map(a=>a.href).find(h=>/^https:\\/\\/live\\.douyin\\.com\\/\\d+/.test(h)) || ''");
            string? liveUrl=JsonSerializer.Deserialize<string>(link);
            if(!string.IsNullOrEmpty(liveUrl))
            {
                string previous=roomKey;Browser.CoreWebView2.Navigate(liveUrl);
                await WaitUntilAsync(()=>roomKey!=previous&&roomKey.Contains("live.douyin.com"),30000,"公开直播间加载超时");
                report.Add("Public livestream opened: "+roomKey);await Task.Delay(5000);
            }
            VideoState? state=null;
            for(int attempt=0;attempt<30;attempt++){state=await PlayerCall("state");if(state.Ok&&state.Playing)break;await Task.Delay(500);}
            if(state is null||!state.Ok||!state.Playing)
            {
                report.Add("UNVERIFIED live playback: no loaded playing HTML video; login or user-selected live room may be required");
                string summary=await Browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({title:document.title,videoCount:document.querySelectorAll('video').length,text:document.body?.innerText.slice(0,700)})");
                report.Add("Public page diagnostics: "+summary);
            }
            else
            {
                report.Add($"PASS embedded public video playing ({state.VideoWidth}x{state.VideoHeight})");
                var focus=await EnterPureAsync();if(focus is null)throw new Exception("直播视频纯画面模式未能启用");
                if(!await PerformPreflightAsync())throw new Exception("公开直播全画面识别自检未通过");
                report.Add("PASS public livestream preflight, full-video OCR and region preview");
                var frame=await CaptureAsync();
                File.WriteAllBytes(Path.Combine(storage.Root,"douyin-video.png"),frame.Png);
                report.Add("PASS pure-video mode and live-frame capture");
                StopRecognition("公开直播已暂停，自动返回网页");await pageRestore;
                var restored=await PlayerCall("state");
                if(restored.Pure)throw new Exception("暂停未退出纯视频模式");
                report.Add("PASS pause automatically restores public webpage");
            }
        }
        catch(Exception ex){report.Add("UNVERIFIED "+ex.Message);storage.Log("Douyin compatibility check",ex);}
        finally{File.WriteAllLines(Path.Combine(storage.Root,"douyin-check.txt"),report);Close();}
    }
}
