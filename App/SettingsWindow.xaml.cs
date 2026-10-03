using System.Globalization;

namespace ValorantHelper;

public partial class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly string dataRoot;
    public SettingsWindow(AppSettings settings,string dataRoot)
    {
        this.settings=settings;this.dataRoot=dataRoot;InitializeComponent();
        Services.ThemeManager.InstallChrome(this);
        IntervalBox.Text=settings.IntervalMs.ToString(CultureInfo.InvariantCulture);ConfidenceBox.Text=Math.Round(settings.MinimumConfidence*100).ToString(CultureInfo.InvariantCulture);
        CooldownBox.Text=settings.CooldownSeconds.ToString(CultureInfo.InvariantCulture);LimitBox.Text=settings.HistoryLimit.ToString(CultureInfo.InvariantCulture);
        foreach(int key in Enumerable.Range(6,7)){ToggleKeyBox.Items.Add("F"+key);CopyKeyBox.Items.Add("F"+key);}
        ToggleKeyBox.SelectedItem="F"+settings.ToggleKey;CopyKeyBox.SelectedItem="F"+settings.CopyKey;TrayCheck.IsChecked=settings.MinimizeToTray;
    }
    private bool Read(TextBox box,int min,int max,out int value)
    {
        if(int.TryParse(box.Text.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out value)&&value>=min&&value<=max)return true;
        ErrorLabel.Text=$"请输入 {min}–{max} 范围内的整数。";box.Focus();box.SelectAll();return false;
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        if(!Read(IntervalBox,150,3000,out int interval)||!Read(ConfidenceBox,50,99,out int confidence)||!Read(CooldownBox,0,3600,out int cooldown)||!Read(LimitBox,10,500,out int limit))return;
        int toggle=int.Parse(((string)ToggleKeyBox.SelectedItem)[1..]),copy=int.Parse(((string)CopyKeyBox.SelectedItem)[1..]);
        if(toggle==copy){ErrorLabel.Text="两个操作需要使用不同的快捷键。";return;}
        settings.IntervalMs=interval;settings.MinimumConfidence=confidence/100f;settings.CooldownSeconds=cooldown;settings.HistoryLimit=limit;
        settings.ToggleKey=toggle;settings.CopyKey=copy;settings.MinimizeToTray=TrayCheck.IsChecked==true;settings.Sanitize();DialogResult=true;
    }
    private void ExportLog_Click(object sender,RoutedEventArgs e)
    {
        string file=Path.Combine(dataRoot,"diagnostic.log");
        if(!File.Exists(file)){ErrorLabel.Text="当前还没有诊断日志。";return;}
        var dialog=new Microsoft.Win32.SaveFileDialog{Title="导出诊断日志",Filter="文本日志|*.txt",FileName="抢码助手诊断日志.txt"};
        if(dialog.ShowDialog(this)==true){try{File.Copy(file,dialog.FileName,true);ErrorLabel.Text="日志已导出。";}catch(Exception ex){ErrorLabel.Text="导出失败："+ex.Message;}}
    }
}
