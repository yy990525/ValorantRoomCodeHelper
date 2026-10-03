using Microsoft.Web.WebView2.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ValorantHelper.Services;

namespace ValorantHelper;

public partial class MainWindow : Window
{
    private readonly Storage storage;
    private readonly AppSettings settings;
    private readonly OcrService ocr = new();
    private readonly RecognitionGate gate = new();
    private readonly GameInput gameInput = new();
    private readonly IGameInputPlatform inputPlatform;
    private bool bindingGame, filling;
    private readonly ObservableCollection<HistoryItem> history;
    private readonly bool testing;
    private readonly string[] arguments;
    private Task? ocrInitialization;
    private Task? loop;
    private Task pageRestore = Task.CompletedTask;
    private CancellationTokenSource? loopCancel;
    private Hotkeys? hotkeys;
    private System.Windows.Forms.NotifyIcon? tray;
    private bool browserReady, ocrReady, pure, running, selecting, closed, busy, copying, demo;
    private int generation, videoSerial;
    private string roomKey = "", currentCode = "", playerScript = "";
    private NormalizedRegion? region;
    private Point selectionStart;
    private VideoState? selectionVideo;
    private BitmapSource? selectionBitmap;
    private HistoryItem? currentItem;
    private long totalFrames;
    private readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };

    public MainWindow(bool testing = false, string[]? arguments = null)
    {
        inputPlatform=testing ? new FakeGameInput() : gameInput;
        this.testing = testing; this.arguments = arguments ?? Array.Empty<string>();
        string dataRoot=Path.Combine(AppContext.BaseDirectory,testing ? this.arguments.Contains("--douyin-check")?"LiveCheckData":"TestData" : "Data");
        storage=new Storage(dataRoot); settings=testing ? new AppSettings{Sound=false,MinimizeToTray=false} : storage.Load();
        history=new(settings.History);
        ThemeManager.Apply(settings.Theme);
        InitializeComponent();ThemeManager.InstallChrome(this);
        UpdateThemeButton();
        UrlBox.Text=settings.LastUrl;AutoCopyCheck.IsChecked=settings.AutoCopy;SoundCheck.IsChecked=settings.Sound;
        FullVideoCheck.IsChecked=settings.FullVideo;AutoFillCheck.IsChecked=settings.AutoFill;
        TopmostCheck.IsChecked=settings.AlwaysOnTop;StableCheck.IsChecked=settings.StableConfirm;Topmost=settings.AlwaysOnTop;
        HistoryList.ItemsSource=history;RefreshHistory();ApplyGateSettings();
        Loaded+=InitializeAsync;
        Closing+=OnClosing;
        Closed+=OnClosed;
        StateChanged+=(s,e)=>{if(WindowState==WindowState.Minimized&&settings.MinimizeToTray&&!testing&&tray is not null)Hide();};
        PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape&&selecting){CancelSelection();StopRecognition("已取消框选，返回网页");e.Handled=true;}};
    }

    public void LogException(Exception ex)=>storage.Log("应用异常",ex);
    private void SetStatus(string text)=>StatusBar.Text=text;
    private void Save()
    {
        if(testing)return;
        try {settings.History=history.Take(settings.HistoryLimit).ToList();storage.Save(settings);}
        catch(Exception ex){storage.Log("保存设置失败",ex);SetStatus("无法保存设置，请检查软件文件夹是否可写");}
    }
    private void ApplyGateSettings()
    {
        gate.RequiredFrames=settings.StableConfirm?2:1;gate.MinimumConfidence=settings.MinimumConfidence;
        gate.DuplicateCooldown=TimeSpan.FromSeconds(settings.CooldownSeconds);gate.MaxFrameGap=TimeSpan.FromMilliseconds(Math.Max(3000,settings.IntervalMs*3));
    }
    private void RefreshHistory(){EmptyHistory.Visibility=history.Count==0?Visibility.Visible:Visibility.Collapsed;HistoryList.Items.Refresh();}
    private void RefreshControls()
    {
        ToggleButton.IsEnabled=browserReady&&ocrReady&&!selecting&&!busy;
        SelectButton.IsEnabled=browserReady&&!selecting&&!busy;
        ToggleButton.Content=running?"Ⅱ  暂停识别":"▶  开始识别";
        StateLabel.Text=running?"●  识别中":"●  已暂停";
        StateLabel.SetResourceReference(TextBlock.ForegroundProperty,running?"Success":"Muted");
        NavigationBar.Visibility=Visibility.Visible;
        PureBadge.Visibility=pure?Visibility.Visible:Visibility.Collapsed;
        ReturnButton.Visibility=pure?Visibility.Visible:Visibility.Collapsed;
        VideoTitle.Text=pure?"直播画面":demo?"本地试用视频":"抖音直播";
        CopyButton.IsEnabled=CodeExtractor.IsValid(currentCode);
        RegionLabel.Text=settings.FullVideo?"全视频范围 · 无需框选":region is null?"请框选房间码位置":"区域已保存 · 仅识别游戏画面";
        FillButton.IsEnabled=CodeExtractor.IsValid(currentCode)&&!filling&&!bindingGame;
        BindGameButton.IsEnabled=!bindingGame&&!filling;
    }

    private async void InitializeAsync(object sender,RoutedEventArgs e)
    {
        try
        {
            playerScript=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Web","player.js"));
            ocrInitialization=Task.Run(()=>ocr.Initialize());
            _=ObserveOcrInitialization();
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(storage.Root,"BrowserData"),new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await Browser.EnsureCoreWebView2Async(environment);
            if(closed)return;
            Browser.ZoomFactor=1;
            Browser.CoreWebView2.Settings.IsZoomControlEnabled=false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            Browser.CoreWebView2.Settings.AreHostObjectsAllowed=false;
            Browser.CoreWebView2.Settings.IsWebMessageEnabled=false;
            Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled=false;
            Browser.CoreWebView2.NavigationStarting+=NavigationStarting;
            Browser.CoreWebView2.NavigationCompleted+=NavigationCompleted;
            Browser.CoreWebView2.NewWindowRequested+=(s,args)=>{args.Handled=true;if(TryDouyinUrl(args.Uri,out var uri)){StopRecognition("切换网页，识别已暂停",false);Browser.CoreWebView2.Navigate(uri!.AbsoluteUri);}};
            Browser.CoreWebView2.ProcessFailed+=(s,args)=>{StopRecognition("浏览器进程异常，请重新打开软件",false);pure=false;browserReady=false;RefreshControls();storage.Log("WebView2 ProcessFailed: "+args.ProcessFailedKind);};
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping("vrh-demo.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
            browserReady=true;
            if(!testing)
            {
                hotkeys=new Hotkeys(this);hotkeys.Toggle+=()=>Toggle_Click(this,new RoutedEventArgs());hotkeys.Copy+=()=>Copy_Click(this,new RoutedEventArgs());
                RegisterHotkeys();SetupTray();
            }
            RefreshControls();
            if(testing&&arguments.Contains("--douyin-check")){NavigateTo("https://live.douyin.com/");_=RunDouyinCheckAsync();}
            else if(testing){OpenDemo();_=RunSmokeTestAsync();}
            else NavigateTo(settings.LastUrl);
        }
        catch(Exception ex)
        {
            storage.Log("初始化浏览器失败",ex);SetStatus("浏览器初始化失败："+ex.Message);
            RecognitionHint.Text="需要 Microsoft Edge WebView2 Runtime。请查看使用说明。";RefreshControls();
            if(testing){File.WriteAllText(Path.Combine(storage.Root,"smoke-failed.txt"),ex.ToString());Close();}
        }
    }
    private async Task ObserveOcrInitialization()
    {
        try {await ocrInitialization!;if(closed)return;ocrReady=true;RefreshControls();SetStatus("OCR 已就绪，播放直播后可直接开始识别");}
        catch(Exception ex){storage.Log("OCR 初始化失败",ex);RecognitionHint.Text="OCR 初始化失败，请完整解压软件和模型文件。";SetStatus("OCR 无法加载："+ex.Message);if(testing){File.WriteAllText(Path.Combine(storage.Root,"smoke-failed.txt"),ex.ToString());Close();}}
    }
    private void RegisterHotkeys()
    {
        string warning=hotkeys?.Register(settings.ToggleKey,settings.CopyKey)??"";
        HotkeyLabel.Text=$"F{settings.ToggleKey} 开始 / 暂停    F{settings.CopyKey} 复制最新码";
        if(warning.Length>0)SetStatus(warning);
    }
    private void SetupTray()
    {
        tray=new System.Windows.Forms.NotifyIcon { Text="无畏契约抢码助手",Visible=true };
        tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application;
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示窗口",null,(s,e)=>Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("开始 / 暂停",null,(s,e)=>Dispatcher.Invoke(()=>Toggle_Click(this,new RoutedEventArgs())));
        menu.Items.Add("复制最新码",null,(s,e)=>Dispatcher.Invoke(()=>Copy_Click(this,new RoutedEventArgs())));
        menu.Items.Add("退出",null,(s,e)=>Dispatcher.Invoke(Close));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(RestoreWindow);
    }
    private void RestoreWindow(){Show();WindowState=WindowState.Normal;Activate();}

    private static bool TryDouyinUrl(string text,out Uri? uri)
    {
        uri=null;text=text.Trim();
        if(!text.StartsWith("https://",StringComparison.OrdinalIgnoreCase))text="https://"+text;
        return Uri.TryCreate(text,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo)&&(uri.Host.Equals("douyin.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".douyin.com",StringComparison.OrdinalIgnoreCase));
    }
    private void NavigateTo(string text)
    {
        if(!browserReady)return;
        if(!TryDouyinUrl(text,out var uri)){SetStatus("请输入抖音网页地址，例如 https://live.douyin.com/直播间编号");return;}
        demo=false;StopRecognition("正在打开直播间…",false);settings.LastUrl=uri!.AbsoluteUri;Save();Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
    }
    private void NavigationStarting(object? sender,CoreWebView2NavigationStartingEventArgs e)
    {
        // Normal embedded-page login redirects are allowed; there are no host objects or script-to-host privileges.
        StopRecognition("网页加载中，识别已暂停",false);CancelSelection();pure=false;region=null;videoSerial=0;RefreshControls();
    }
    private async void NavigationCompleted(object? sender,CoreWebView2NavigationCompletedEventArgs e)
    {
        if(closed)return;
        if(!e.IsSuccess){SetStatus("网页加载失败："+e.WebErrorStatus+"，可刷新重试");return;}
        try
        {
            await Browser.CoreWebView2.ExecuteScriptAsync(playerScript);
            var uri=new Uri(Browser.CoreWebView2.Source);
            roomKey=uri.GetLeftPart(UriPartial.Path).TrimEnd('/');demo=uri.Host=="vrh-demo.local";
            if(!demo&&TryDouyinUrl(uri.AbsoluteUri,out _)){UrlBox.Text=uri.AbsoluteUri;settings.LastUrl=uri.AbsoluteUri;Save();}
            region=settings.Regions.TryGetValue(roomKey,out var saved)&&saved.IsValid?saved:null;
            RefreshControls();RegionPreview.Source=null;PreviewLabel.Text="尚未取帧";PreflightStatus.Text="开始识别前会自动检查";
            SetStatus(demo?"本地试用：直接开始识别，也可切换到框选模式":"网页已打开，请播放直播后开始识别");
        }
        catch(Exception ex){storage.Log("页面准备失败",ex);SetStatus("页面准备失败，可刷新重试");}
    }
    private void Navigate_Click(object sender,RoutedEventArgs e)=>NavigateTo(UrlBox.Text);
    private void UrlBox_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){NavigateTo(UrlBox.Text);e.Handled=true;}}
    private void Back_Click(object sender,RoutedEventArgs e){if(browserReady&&Browser.CoreWebView2.CanGoBack)Browser.CoreWebView2.GoBack();}
    private void Reload_Click(object sender,RoutedEventArgs e){if(browserReady){StopRecognition("刷新网页，识别已暂停",false);Browser.CoreWebView2.Reload();}}
    private void Demo_Click(object sender,RoutedEventArgs e){if(browserReady)OpenDemo();}
    private void OpenDemo(){demo=true;Browser.CoreWebView2.Navigate("https://vrh-demo.local/demo.html");}

    private async Task<VideoState> PlayerCall(string method)
    {
        if(!browserReady)throw new InvalidOperationException("浏览器尚未就绪");
        string result=await Browser.CoreWebView2.ExecuteScriptAsync($"window.__vrhPlayer ? window.__vrhPlayer.{method}() : ({{ok:false,reason:'网页尚未准备好，请等待加载完成。'}})");
        return JsonSerializer.Deserialize<VideoState>(result,json)??new(){Reason="未找到视频"};
    }
    private async Task<VideoState?> EnterPureAsync()
    {
        await pageRestore;
        if(closed)return null;
        int preparation=generation;
        var state=await PlayerCall("enter");
        if(preparation!=generation||closed){if(!closed)await RestoreWebPageAsync(generation);return null;}
        if(!state.Ok||!state.Pure){SetStatus(state.Reason??"未找到可播放的直播画面");return null;}
        pure=true;videoSerial=state.Serial;RefreshControls();
        // Wait for two paint frames so the first screenshot cannot include the previously visible chat.
        await Task.Delay(120);
        return await PlayerCall("state");
    }
    private async void Return_Click(object sender,RoutedEventArgs e)
    {
        StopRecognition("已返回网页，识别暂停");CancelSelection();
        await pageRestore;
    }

    private async Task<(byte[] Png,VideoState State,BitmapSource Image)> CaptureAsync()
    {
        var before=await PlayerCall("state");
        if(!before.Ok||!before.Pure)throw new InvalidOperationException("纯画面模式失效，请重新框选区域");
        if(!before.Playing)throw new InvalidOperationException("视频已暂停或尚未播放，请返回网页恢复播放");
        using var stream=new MemoryStream();
        await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        var after=await PlayerCall("state");
        if(!after.Ok||!after.Pure||after.Serial!=before.Serial||Math.Abs(after.ViewportWidth-before.ViewportWidth)>.1||Math.Abs(after.ViewportHeight-before.ViewportHeight)>.1||after.VideoWidth!=before.VideoWidth||after.VideoHeight!=before.VideoHeight)throw new InvalidOperationException("画面或窗口尺寸发生变化，请重试框选");
        var png=stream.ToArray();var image=new BitmapImage();
        using(var input=new MemoryStream(png)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=input;image.EndInit();image.Freeze();}
        if(image.PixelWidth<10||image.PixelHeight<10)throw new InvalidDataException("直播截图为空");
        return(png,after,image);
    }

    private async void Select_Click(object sender,RoutedEventArgs e)
    {
        if(busy||!browserReady)return;StopRecognition("正在准备框选画面…",false);busy=true;RefreshControls();
        try
        {
            if(await EnterPureAsync() is null)return;
            var frame=await CaptureAsync();selectionVideo=frame.State;selectionBitmap=frame.Image;
            SelectionImage.Source=selectionBitmap;Browser.Visibility=Visibility.Hidden;
            SelectionLayer.Visibility=Visibility.Visible;SelectionBorder.Visibility=Visibility.Collapsed;selecting=true;
            SetStatus("拖动框住房间码，建议只框六位字符；Esc 取消");
        }
        catch(Exception ex){storage.Log("准备框选失败",ex);SetStatus(ex.Message);}
        finally{busy=false;if(!selecting&&pure&&!running&&!closed){pageRestore=RestoreWebPageAsync(generation);await pageRestore;}RefreshControls();}
    }
    private void Selection_Down(object sender,MouseButtonEventArgs e)
    {
        if(!selecting)return;selectionStart=e.GetPosition(SelectionCanvas);SelectionCanvas.CaptureMouse();
        Canvas.SetLeft(SelectionBorder,selectionStart.X);Canvas.SetTop(SelectionBorder,selectionStart.Y);SelectionBorder.Width=0;SelectionBorder.Height=0;SelectionBorder.Visibility=Visibility.Visible;
    }
    private void Selection_Move(object sender,MouseEventArgs e)
    {
        if(!selecting||!SelectionCanvas.IsMouseCaptured)return;
        var p=e.GetPosition(SelectionCanvas);p.X=Math.Clamp(p.X,0,SelectionCanvas.ActualWidth);p.Y=Math.Clamp(p.Y,0,SelectionCanvas.ActualHeight);
        Canvas.SetLeft(SelectionBorder,Math.Min(selectionStart.X,p.X));Canvas.SetTop(SelectionBorder,Math.Min(selectionStart.Y,p.Y));
        SelectionBorder.Width=Math.Abs(p.X-selectionStart.X);SelectionBorder.Height=Math.Abs(p.Y-selectionStart.Y);
    }
    private void Selection_Up(object sender,MouseButtonEventArgs e)
    {
        if(!selecting||!SelectionCanvas.IsMouseCaptured||selectionVideo is null||selectionBitmap is null)return;
        SelectionCanvas.ReleaseMouseCapture();
        if(SelectionBorder.Width<12||SelectionBorder.Height<10){SetStatus("识别区域太小，请重新拖动框选");return;}
        var state=selectionVideo;double sx=state.ViewportWidth/SelectionCanvas.ActualWidth,sy=state.ViewportHeight/SelectionCanvas.ActualHeight;
        double left=Math.Max(state.X,Canvas.GetLeft(SelectionBorder)*sx),top=Math.Max(state.Y,Canvas.GetTop(SelectionBorder)*sy);
        double right=Math.Min(state.X+state.Width,(Canvas.GetLeft(SelectionBorder)+SelectionBorder.Width)*sx),bottom=Math.Min(state.Y+state.Height,(Canvas.GetTop(SelectionBorder)+SelectionBorder.Height)*sy);
        var selected=new NormalizedRegion((left-state.X)/state.Width,(top-state.Y)/state.Height,(right-left)/state.Width,(bottom-top)/state.Height);
        if(!selected.IsValid){SetStatus("请在游戏视频范围内框选，避开黑边");return;}
        region=selected;settings.FullVideo=false;FullVideoCheck.IsChecked=false;settings.Regions[roomKey]=selected;Save();gate.ResetPending();CancelSelection();StopRecognition("区域已保存，点击开始识别");
    }
    private void CancelSelection()
    {
        SelectionCanvas.ReleaseMouseCapture();selecting=false;SelectionLayer.Visibility=Visibility.Collapsed;SelectionImage.Source=null;
        selectionBitmap=null;selectionVideo=null;Browser.Visibility=Visibility.Visible;RefreshControls();
    }

    private async void Toggle_Click(object sender,RoutedEventArgs e)
    {
        if(running){StopRecognition("识别已暂停，最新房间码保留");return;}
        if(busy||selecting||!ocrReady||!browserReady)return;
        if(!settings.FullVideo&&region is null){Select_Click(sender,e);return;}
        await StartRecognitionAsync();
    }
    private async Task StartRecognitionAsync()
    {
        if(busy||running||(!settings.FullVideo&&region is null))return;
        busy=true;int preparation=generation;RefreshControls();
        try
        {
            if(loop is not null)await loop;
            if(!await PerformPreflightAsync()||closed||preparation!=generation)return;
            gate.ResetPending();ApplyGateSettings();running=true;totalFrames=0;generation++;
            loopCancel?.Dispose();loopCancel=new();int session=generation;
            RecognitionHint.Text=demo?"本地试用 · 演示码会自动切换":settings.FullVideo?"识别整个视频 · 多个候选时暂停发布":"仅识别框选位置，弹幕和聊天不参与识别";
            SetStatus("识别已开始");loop=RecognitionLoopAsync(session,loopCancel.Token);
        }
        catch(Exception ex){storage.Log("启动识别失败",ex);SetStatus(ex.Message);}
        finally{busy=false;if(!running&&pure&&!closed){pageRestore=RestoreWebPageAsync(generation);await pageRestore;}RefreshControls();}
    }
    public void StopRecognition(string reason,bool restorePage=true)
    {
        running=false;generation++;loopCancel?.Cancel();gate.ResetPending();
        if(!closed){RecognitionHint.Text="识别已暂停，网页弹幕和聊天将恢复";RefreshControls();SetStatus(reason);}
        if(restorePage&&pure&&browserReady&&!closed)pageRestore=RestoreWebPageAsync(generation);
    }
    private async Task RecognitionLoopAsync(int session,CancellationToken token)
    {
        try
        {
            while(!token.IsCancellationRequested&&running&&session==generation)
            {
                if(WindowState==WindowState.Minimized||!IsVisible)
                {
                    RecognitionHint.Text="窗口隐藏时暂缓取帧，恢复窗口后继续";gate.ResetPending();await Task.Delay(500,token);continue;
                }
                var frame=await CaptureAsync();token.ThrowIfCancellationRequested();
                if(frame.State.Serial!=videoSerial){StopRecognition("播放器已更换，请重新自检后开始");break;}
                var crop=MapRecognitionCrop(frame.State,frame.Image);
                UpdatePreview(frame.Image,crop);
                var result=await Task.Run(()=>ocr.Recognize(frame.Png,crop),token);
                token.ThrowIfCancellationRequested();if(session!=generation)break;
                totalFrames++;
                var decision=gate.Observe(result.Candidates,DateTimeOffset.UtcNow);
                switch(decision.State)
                {
                    case RecognitionState.Stable:
                        await PublishAsync(decision.Candidate!,session);break;
                    case RecognitionState.Conflict:
                        RecognitionHint.Text="发现多个候选，已阻止复制与填入；可改为框选模式";break;
                    case RecognitionState.LowConfidence:
                        RecognitionHint.Text="结果不够清晰，等待下一帧确认";break;
                    case RecognitionState.Confirming:
                        RecognitionHint.Text="正在连续确认："+decision.Candidate!.Code;break;
                    case RecognitionState.Waiting:
                        RecognitionHint.Text="等待符合 AAA000 格式的房间码";break;
                    default:
                        RecognitionHint.Text="持续监听 · 相同房间码不会重复复制";break;
                }
                token.ThrowIfCancellationRequested();
                PreflightStatus.Text=$"✓ 浏览器 / OCR / 播放 / 截图正常 · 已检查 {totalFrames} 帧";
                SetStatus($"{(demo?"本地试用":"画面识别")} · 已检查 {totalFrames} 帧 · 最近 OCR {result.ElapsedMs} ms");
                await Task.Delay(settings.IntervalMs,token);
            }
        }
        catch(OperationCanceledException) { }
        catch(Exception ex){if(!closed&&session==generation&&!token.IsCancellationRequested){storage.Log("识别暂停",ex);StopRecognition(ex.Message);}}
    }
    private async Task PublishAsync(Candidate candidate,int session)
    {
        currentCode=candidate.Code;LatestCode.Text=currentCode;CopyStatus.Text="已发现，尚未复制";CopyStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        currentItem=new(){Code=currentCode,Time=DateTimeOffset.Now,Source=demo?"本地试用":"游戏画面",Status="已发现"};
        history.Insert(0,currentItem);while(history.Count>settings.HistoryLimit)history.RemoveAt(history.Count-1);
        LatestTime.Text=$"{currentItem.Source} · {currentItem.DisplayTime}";RefreshHistory();RefreshControls();
        if(settings.AutoCopy)
        {
            bool success=await WriteCodeAsync(candidate.Code,()=>!closed&&running&&session==generation&&currentCode==candidate.Code);
            if(session!=generation||closed)return;
            currentItem.Status=success?"已复制":"复制失败";CopyStatus.Text=success?"✓  已复制到剪贴板":"复制失败，请点击再次复制";
            CopyStatus.SetResourceReference(TextBlock.ForegroundProperty,success?"Success":"Accent");
            if(success&&settings.AutoFill)
            {
                gate.MarkPublished(candidate.Code,DateTimeOffset.UtcNow);
                await FillCodeAsync(candidate.Code,()=>!closed&&running&&settings.AutoFill&&session==generation&&currentCode==candidate.Code,currentItem);
            }
            if(session!=generation||closed)return;
            if(success&&settings.Sound)System.Media.SystemSounds.Asterisk.Play();
        }
        gate.MarkPublished(candidate.Code,DateTimeOffset.UtcNow);
        RefreshHistory();Save();
    }
    private async void Copy_Click(object sender,RoutedEventArgs e)=>await CopyCodeAsync(currentCode,currentItem);
    private async Task CopyCodeAsync(string code,HistoryItem? item)
    {
        if(copying||!CodeExtractor.IsValid(code))return;copying=true;
        try
        {
            bool success=await WriteCodeAsync(code,()=>!closed);
            if(item is not null)item.Status=success?"已复制":"复制失败";
            if(code==currentCode){CopyStatus.Text=success?"✓  已复制到剪贴板":"剪贴板被占用，请稍后重试";CopyStatus.SetResourceReference(TextBlock.ForegroundProperty,success?"Success":"Accent");}
            SetStatus(success?$"已复制 {code}":"剪贴板写入失败，请稍后再试");RefreshHistory();Save();
        }
        finally{copying=false;}
    }
    private async void History_DoubleClick(object sender,MouseButtonEventArgs e){if(HistoryList.SelectedItem is HistoryItem item)await CopyCodeAsync(item.Code,item);}
    private void ClearHistory_Click(object sender,RoutedEventArgs e){history.Clear();RefreshHistory();Save();}
    private void QuickSetting_Click(object sender,RoutedEventArgs e)
    {
        settings.AutoFill=AutoFillCheck.IsChecked==true;
        settings.AutoCopy=AutoCopyCheck.IsChecked==true;settings.Sound=SoundCheck.IsChecked==true;
        settings.AlwaysOnTop=TopmostCheck.IsChecked==true;settings.StableConfirm=StableCheck.IsChecked==true;Topmost=settings.AlwaysOnTop;
        if(settings.AutoFill){settings.AutoCopy=true;settings.AlwaysOnTop=false;AutoCopyCheck.IsChecked=true;TopmostCheck.IsChecked=false;Topmost=false;}
        ApplyGateSettings();gate.ResetPending();Save();
    }
    private async void Settings_Click(object sender,RoutedEventArgs e)
    {
        StopRecognition("修改设置，识别已暂停");
        await pageRestore;
        var dialog=new SettingsWindow(settings,storage.Root){Owner=this};
        if(dialog.ShowDialog()==true){ApplyGateSettings();RegisterHotkeys();Save();SetStatus("设置已保存");}
    }
    private void OnClosing(object? sender,CancelEventArgs e){closed=true;StopRecognition("正在退出",false);Save();}
    private void OnClosed(object? sender,EventArgs e)
    {
        hotkeys?.Dispose();if(tray is not null){tray.Visible=false;tray.Dispose();}
        Browser.Dispose();
        _=Task.Run(async()=>{try{if(ocrInitialization is not null)await ocrInitialization;if(loop is not null)await loop;}catch{}finally{ocr.Dispose();}});
    }

    public sealed class VideoState
    {
        public bool Ok{get;set;}public bool Pure{get;set;}public bool Playing{get;set;}public string? Reason{get;set;}
        public int Serial{get;set;}public double X{get;set;}public double Y{get;set;}public double Width{get;set;}public double Height{get;set;}
        public double ViewportWidth{get;set;}public double ViewportHeight{get;set;}public int VideoWidth{get;set;}public int VideoHeight{get;set;}
    }
}
