using System.Text.Json;

namespace ValorantHelper.Services;

public sealed class Storage
{
    public string Root { get; }
    private readonly JsonSerializerOptions options = new() { WriteIndented = true };
    public Storage(string root) { Root = root; Directory.CreateDirectory(root); }
    public AppSettings Load()
    {
        try
        {
            string file=Path.Combine(Root,"settings.json");
            var settings=File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file),options) ?? new() : new AppSettings();
            settings.Sanitize(); return settings;
        }
        catch(Exception ex) { Log("读取设置失败，使用默认设置",ex); return new(); }
    }
    public void Save(AppSettings settings)
    {
        settings.Sanitize();
        string file=Path.Combine(Root,"settings.json"),temp=file+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(settings,options));
        File.Move(temp,file,true);
    }
    public void Log(string message,Exception? exception=null)
    {
        try
        {
            string log=Path.Combine(Root,"diagnostic.log");
            if(File.Exists(log)&&new FileInfo(log).Length>1024*1024) File.Move(log,log+".old",true);
            File.AppendAllText(log,$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}{(exception is null ? "" : "\n"+exception)}\n");
        }
        catch { /* Logging must not interrupt recognition. */ }
    }
}
