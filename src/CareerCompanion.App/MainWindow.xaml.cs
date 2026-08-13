using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using CareerCompanion.Core.Providers.Fifa18;

namespace CareerCompanion.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly FrameworkElement[] _pages;
    private Fifa18SaveWatcher? _fifaWatcher;
    private bool _fifaScanBusy;
    public MainWindow()
    {
        InitializeComponent();var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TouchlineCareerCompanion");var db=new Database(Path.Combine(root,"career-world.db"));db.Migrate();new DemoSeeder(db).EnsureDemo();_vm=new MainViewModel(db);DataContext=_vm;_pages=[HomePage,PreMatchPage,CareerPage,MatchPage,SquadPage,MessagesPage,ManagerPage,PressPage,NewsPage,SocialPage,TimelinePage,SettingsPage,DebugPage];Loaded+=(_,_)=>RestartFifaWatcher();Closed+=(_,_)=>_fifaWatcher?.Dispose();
    }
    private void Navigation_Changed(object sender,SelectionChangedEventArgs e){if(_pages is null)return;for(var i=0;i<_pages.Length;i++)_pages[i].Visibility=i==Navigation.SelectedIndex?Visibility.Visible:Visibility.Collapsed;if(Navigation.SelectedIndex==12)_vm.Refresh();}
    private void Guard(Action action){try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"Touchline",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    private void CreateCareer_Click(object sender,RoutedEventArgs e)=>Guard(_vm.CreateCareer);
    private void OpenCareer_Click(object sender,RoutedEventArgs e)=>Guard(_vm.Refresh);
    private void AddCharacter_Click(object sender,RoutedEventArgs e)=>Guard(_vm.AddCharacter);
    private void ProcessMatch_Click(object sender,RoutedEventArgs e)=>Guard(_vm.ProcessMatch);
    private async void SendMessage_Click(object sender,RoutedEventArgs e){try{await _vm.SendMessageAsync();}catch(Exception ex){MessageBox.Show(ex.Message,"Touchline",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    private void RecordPress_Click(object sender,RoutedEventArgs e)=>Guard(_vm.RecordPress);
    private void SaveSettings_Click(object sender,RoutedEventArgs e){Guard(()=>_vm.SaveSettings(ApiKeyBox.Password));RestartFifaWatcher();}
    private void Backup_Click(object sender,RoutedEventArgs e){var dialog=new SaveFileDialog{Filter="Touchline backup (*.db)|*.db",FileName=$"touchline-backup-{DateTime.Now:yyyyMMdd-HHmm}.db"};if(dialog.ShowDialog()==true)Guard(()=>_vm.Backup(dialog.FileName));}
    private void EditProfile_Click(object sender,RoutedEventArgs e){if(_vm.SelectedCharacter is null){MessageBox.Show("Select a character in Squad first.","Touchline");return;}var dialog=new ProfileEditorWindow(_vm.SelectedCharacter){Owner=this};if(dialog.ShowDialog()==true){Guard(()=>dialog.Save());_vm.Refresh();}}
    private void Restore_Click(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Filter="Touchline backup (*.db)|*.db"};if(dialog.ShowDialog()!=true)return;if(MessageBox.Show("Restore this backup? Current local data will be replaced. Export a backup first if needed.","Restore backup",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes)Guard(()=>_vm.Restore(dialog.FileName));}
    private async void ScanFifa_Click(object sender,RoutedEventArgs e)=>await ScanFifaAsync(false);
    private void AutoFifaWatch_Click(object sender,RoutedEventArgs e){_vm.SaveSettings(null);RestartFifaWatcher();}
    private void FifaPreference_Click(object sender,RoutedEventArgs e)=>_vm.SaveSettings(null);
    private async Task ScanFifaAsync(bool automatic)
    {
        if(_fifaScanBusy)return;_fifaScanBusy=true;
        try
        {
            var result=await _vm.ScanFifaSaveAsync(automatic);
            if(result.Disposition is Fifa18ScanDisposition.CareerMismatch or Fifa18ScanDisposition.NoCareerSelected&&!automatic)
            {
                var answer=MessageBox.Show($"The FIFA save belongs to {result.Parsed.PlayerName} at {result.Parsed.ClubName}. Create and link a companion career for it?","Link FIFA career",MessageBoxButton.YesNo,MessageBoxImage.Question);
                if(answer==MessageBoxResult.Yes){_vm.CreateCareerFromPendingFifa();Navigation.SelectedIndex=3;}
            }
            else if(result.Disposition==Fifa18ScanDisposition.MatchDetected&&!automatic)Navigation.SelectedIndex=3;
        }
        catch(Exception ex){_vm.MarkFifaSyncFailure(ex.Message);if(!automatic)MessageBox.Show(ex.Message,"FIFA 18 sync",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{_fifaScanBusy=false;}
    }
    private void ReviewFifaMatch_Click(object sender,RoutedEventArgs e)=>Guard(()=>{_vm.PopulatePendingMatchForReview();Navigation.SelectedIndex=3;});
    private void TalkTeammate_Click(object sender,RoutedEventArgs e){if(_vm.PrepareConversation(CareerCompanion.Core.Domain.CharacterType.Teammate,CareerCompanion.Core.Domain.SceneType.PreMatch))Navigation.SelectedIndex=5;else MessageBox.Show("No active teammate is available.","Touchline");}
    private void TalkManager_Click(object sender,RoutedEventArgs e){if(_vm.PrepareConversation(CareerCompanion.Core.Domain.CharacterType.Manager,CareerCompanion.Core.Domain.SceneType.ManagerOffice))Navigation.SelectedIndex=5;else MessageBox.Show("Add a manager character first.","Touchline");}
    private void OpenPress_Click(object sender,RoutedEventArgs e)=>Navigation.SelectedIndex=7;
    private void RestartFifaWatcher()
    {
        _fifaWatcher?.Dispose();_fifaWatcher=null;if(!_vm.AutoFifaWatch||!Directory.Exists(_vm.FifaSettingsDirectory))return;
        _fifaWatcher=new Fifa18SaveWatcher(_vm.FifaSettingsDirectory);_fifaWatcher.SaveChanged+=(_,_)=>Dispatcher.InvokeAsync(async()=>await ScanFifaAsync(true));
    }
}
