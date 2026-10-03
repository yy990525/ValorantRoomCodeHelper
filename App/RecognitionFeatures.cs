using System.Windows.Media.Imaging;
using ValorantHelper.Services;

namespace ValorantHelper;

public partial class MainWindow
{
    private (int X,int Y,int Width,int Height) MapRecognitionCrop(VideoState state,BitmapSource image)
    {
        var selected=settings.FullVideo?new NormalizedRegion(0,0,1,1):region;
        if(selected is null||!selected.IsValid)throw new InvalidOperationException("请框选识别区域，或打开全画面模式");
        double sx=image.PixelWidth/state.ViewportWidth,sy=image.PixelHeight/state.ViewportHeight;
        return selected.Map(state.X*sx,state.Y*sy,state.Width*sx,state.Height*sy,image.PixelWidth,image.PixelHeight);
    }
    private void UpdatePreview(BitmapSource image,(int X,int Y,int Width,int Height) crop)
    {
        var preview=new CroppedBitmap(image,new Int32Rect(crop.X,crop.Y,crop.Width,crop.Height));preview.Freeze();
        RegionPreview.Source=preview;
        PreviewLabel.Text=$"{(settings.FullVideo?"全视频范围":"框选范围")} · {crop.Width} × {crop.Height} · {DateTime.Now:HH:mm:ss}";
    }
    private void Mode_Click(object sender,RoutedEventArgs e)
    {
        StopRecognition("识别范围已切换，请开始识别");CancelSelection();
        settings.FullVideo=FullVideoCheck.IsChecked==true;RegionPreview.Source=null;PreviewLabel.Text="开始或自检后刷新预览";
        PreflightStatus.Text="识别范围已切换，等待自检";Save();RefreshControls();
    }
    private async Task<bool> PerformPreflightAsync()
    {
        int session=generation;
        PreflightStatus.Text="正在检查浏览器、OCR、直播播放和取帧…";
        try
        {
            if(!browserReady)throw new InvalidOperationException("浏览器尚未就绪");
            if(!ocrReady)throw new InvalidOperationException("OCR 正在初始化，请稍后再试");
            if(!IsVisible||WindowState==WindowState.Minimized)throw new InvalidOperationException("请恢复助手窗口再开始识别");
            if(!settings.FullVideo&&(region is null||!region.IsValid))throw new InvalidOperationException("请先框选区域，或启用全画面模式");
            var state=await EnterPureAsync()??throw new InvalidOperationException("没有找到直播视频，请先播放直播");
            if(closed||session!=generation)return false;
            if(!state.Playing)throw new InvalidOperationException("视频尚未播放，请返回网页恢复播放");
            var frame=await CaptureAsync();var crop=MapRecognitionCrop(frame.State,frame.Image);
            if(closed||session!=generation)return false;
            if(crop.Width<12||crop.Height<10)throw new InvalidOperationException("识别区域过小，请重新选择");
            UpdatePreview(frame.Image,crop);
            var result=await Task.Run(()=>ocr.Recognize(frame.Png,crop));
            if(closed||session!=generation)return false;
            if(settings.AutoFill&&!demo)
            {
                var error=inputPlatform.CheckTarget();
                GameTargetLabel.Text=error.Length==0?"✓ 游戏输入位置已绑定":error;
                if(error.Length>0)throw new InvalidOperationException(error);
            }
            int count=result.Candidates.Select(c=>c.Code).Distinct().Count();
            PreflightStatus.Text="✓ 浏览器 / OCR / 播放 / 截图 / 区域正常\n"+(count==0?"未发现房间码，开始后等待出现":count>1?"发现多个候选，将阻止发布；建议框选":"发现格式候选，开始后连续确认");
            SetStatus("自检通过，可开始识别");return true;
        }
        catch(Exception ex)
        {
            if(closed||session!=generation)return false;
            PreflightStatus.Text="✕ "+ex.Message;SetStatus("自检未通过："+ex.Message);storage.Log("识别前自检失败",ex);return false;
        }
    }
    private async void Preflight_Click(object sender,RoutedEventArgs e)
    {
        if(busy||selecting||bindingGame)return;
        StopRecognition("正在进行识别前自检");busy=true;RefreshControls();
        try {if(loop is not null)await loop;await PerformPreflightAsync();if(pure){pageRestore=RestoreWebPageAsync(generation);await pageRestore;}}
        finally {busy=false;RefreshControls();}
    }
    private async void BindGame_Click(object sender,RoutedEventArgs e)
    {
        if(bindingGame||filling||busy)return;
        StopRecognition("准备绑定游戏输入框");bindingGame=true;busy=true;int session=generation;RefreshControls();
        try
        {
            if(loop is not null)await loop;
            await pageRestore;
            // Binding always asks the user to point at the field; it never guesses a game menu position.
            for(int remaining=6;remaining>0;remaining--)
            {
                GameTargetLabel.Text=$"{remaining} 秒后绑定：切换到游戏加入房间窗口，将鼠标放在房间码输入框内";
                await Task.Delay(1000);if(closed||session!=generation)return;
            }
            string error=gameInput.BindFromCursor();
            GameTargetLabel.Text=error.Length==0?"✓ 已绑定输入位置 · 保持加入房间窗口打开":error;
            SetStatus(error.Length==0?"游戏输入框已绑定，本次运行有效":error);
        }
        finally {bindingGame=false;busy=false;if(!closed)RefreshControls();}
    }
    private bool ClipboardMatches(string code)
    {
        if(testing)return testClipboard==code;
        try {return System.Windows.Clipboard.ContainsText()&&System.Windows.Clipboard.GetText()==code;}
        catch {return false;}
    }
    private async Task FillCodeAsync(string code,Func<bool> stillCurrent,HistoryItem? item)
    {
        if(filling||!stillCurrent())return;
        if(demo&&!testing){CopyStatus.Text="演示码已复制，不填入真实游戏";return;}
        filling=true;RefreshControls();
        try
        {
            var result=await new GameFill(inputPlatform).FillAsync(code,stillCurrent,()=>ClipboardMatches(code));
            if(closed)return;
            if(item is not null)item.Status=result.Sent?"已发填入":"仅复制";
            if(currentCode==code){CopyStatus.Text=result.Message;CopyStatus.SetResourceReference(TextBlock.ForegroundProperty,result.Sent?"Success":"Accent");}
            GameTargetLabel.Text=result.Sent?"已发送粘贴 · 请在游戏检查并确认加入":result.Message;
            RefreshHistory();Save();
        }
        finally {filling=false;if(!closed)RefreshControls();}
    }
    private async void Fill_Click(object sender,RoutedEventArgs e)
    {
        if(filling||bindingGame||busy||!CodeExtractor.IsValid(currentCode))return;
        StopRecognition("手动填入最新码，识别已暂停");
        if(loop is not null)await loop;
        await pageRestore;
        string code=currentCode;
        if(demo&&!testing){SetStatus("本地演示码不填入真实游戏");return;}
        string error=inputPlatform.CheckTarget();
        if(error.Length>0){GameTargetLabel.Text=error;SetStatus(error);return;}
        settings.AlwaysOnTop=false;TopmostCheck.IsChecked=false;Topmost=false;Save();
        int session=generation;
        if(await WriteCodeAsync(code,()=>!closed&&session==generation))
            await FillCodeAsync(code,()=>!closed&&session==generation,currentItem);
        else CopyStatus.Text="复制失败，未发送填入";
    }
    // Used exclusively by --smoke-test. It sends no OS input and cannot switch to a real game.
    private sealed class FakeGameInput : IGameInputPlatform
    {
        public int Pastes {get;private set;}
        public bool IsForeground=>true;
        public bool ModifiersPressed=>false;
        public string CheckTarget()=>"";
        public bool Activate()=>true;
        public bool ClickInput()=>true;
        public bool SelectAll()=>true;
        public bool Paste(){Pastes++;return true;}
    }
}
