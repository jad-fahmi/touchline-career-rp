namespace CareerCompanion.Core.Providers.Fifa18;

public sealed class Fifa18SaveWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly object _gate=new();
    private bool _disposed;
    public event EventHandler? SaveChanged;
    public string DirectoryPath { get; }

    public Fifa18SaveWatcher(string directory)
    {
        if(!Directory.Exists(directory))throw new DirectoryNotFoundException(directory);
        DirectoryPath=directory;_debounce=new Timer(_=>Raise(),null,Timeout.Infinite,Timeout.Infinite);
        _watcher=new FileSystemWatcher(directory,"Career*"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size,IncludeSubdirectories=false};
        _watcher.Changed+=Changed;_watcher.Created+=Changed;_watcher.Renamed+=Changed;_watcher.EnableRaisingEvents=true;
    }
    private void Changed(object sender,FileSystemEventArgs e){lock(_gate){if(!_disposed)_debounce.Change(1500,Timeout.Infinite);}}
    private void Raise(){lock(_gate){if(_disposed)return;}SaveChanged?.Invoke(this,EventArgs.Empty);}
    public void Dispose(){lock(_gate){if(_disposed)return;_disposed=true;}_watcher.Dispose();_debounce.Dispose();}
}
