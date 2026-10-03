namespace ValorantHelper;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool testing=e.Args.Contains("--smoke-test")||e.Args.Contains("--douyin-check");
        if(!testing)
        {
            mutex=new Mutex(true,"Local\\ValorantRoomCodeHelper-Desktop",out bool created);
            if(!created){System.Windows.MessageBox.Show("抢码助手已经在运行，请查看窗口或系统托盘。","抢码助手");Shutdown();return;}
        }
        var window=new MainWindow(testing,e.Args); MainWindow=window;window.Show();
        DispatcherUnhandledException+=(s,args)=>
        {
            window.LogException(args.Exception);window.StopRecognition("运行遇到异常，识别已暂停");
            System.Windows.MessageBox.Show(window,args.Exception.Message,"发生错误");args.Handled=true;
        };
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose();base.OnExit(e); }
}
