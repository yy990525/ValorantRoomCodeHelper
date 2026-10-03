using System.Windows.Input;

namespace ValorantHelper.Services;

public static class ThemeManager
{
    public static string Current {get;private set;}="Dark";
    public static void Apply(string theme)
    {
        Current=theme=="Light"?"Light":"Dark";
        string[] keys={"Bg","Panel","Line","Text","Muted","Accent","AccentText","Success","SuccessBg","Surface","ButtonBg","ButtonBorder","Hover","Selected","InputBg","Track","Thumb","ThumbHover","TitleBg","BadgeBg","Disabled"};
        string[] dark={"#0E1520","#182230","#304052","#F2F5FA","#A4B3C6","#D9344C","#FFFFFF","#42DBA1","#15382C","#111C2A","#253448","#3B4D64","#30435E","#284463","#111C2A","#283649","#526A86","#7792B3","#121B28","#243146","#6E8099"};
        string[] light={"#F3F6FA","#FFFFFF","#D5DDE9","#162238","#53667E","#D93049","#FFFFFF","#087D55","#E1F3EB","#EDF2F8","#E9EFF7","#CBD6E4","#DBE6F4","#D5E5F8","#FAFCFF","#CBD6E4","#9AAFC6","#6C8AA9","#EAF0F7","#E2EAF5","#718198"};
        var values=Current=="Light"?light:dark;
        System.Windows.Application.Current.Resources["AccentHover"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BF243D"));
        for(int i=0;i<keys.Length;i++)System.Windows.Application.Current.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
    }
    public static void InstallChrome(Window window)
    {
        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,(s,e)=>SystemCommands.CloseWindow(window)));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,(s,e)=>SystemCommands.MinimizeWindow(window)));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,(s,e)=>{if(window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(window);else SystemCommands.MaximizeWindow(window);}));
    }
}
