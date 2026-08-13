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
    private bool _fifaRescanRequested;
    private bool _fifaManualRescanRequested;
    private int _fifaWatcherRecoveryAttempt;
    public MainWindow()
    {
        InitializeComponent();var overrideRoot=Environment.GetEnvironmentVariable("TOUCHLINE_DATA_DIR");var root=string.IsNullOrWhiteSpace(overrideRoot)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TouchlineCareerCompanion"):Path.GetFullPath(overrideRoot);var db=new Database(Path.Combine(root,"career-world.db"));db.Migrate();new DemoSeeder(db).EnsureDemo();_vm=new MainViewModel(db);DataContext=_vm;_pages=[HomePage,WorldInboxPage,PreMatchPage,ReviewInboxPage,CareerPage,MatchPage,SquadPage,MessagesPage,ManagerPage,PressPage,NewsPage,SocialPage,TimelinePage,SettingsPage,DebugPage];Loaded+=async (_,_)=>{RestartFifaWatcher();if(_vm.AutoFifaWatch&&Directory.Exists(_vm.FifaSettingsDirectory))await ScanFifaAsync(true);await _vm.RunAutomaticGenerationAsync();};Closed+=(_,_)=>_fifaWatcher?.Dispose();
    }
    private void Navigation_Changed(object sender,SelectionChangedEventArgs e){if(_pages is null)return;for(var i=0;i<_pages.Length;i++)_pages[i].Visibility=i==Navigation.SelectedIndex?Visibility.Visible:Visibility.Collapsed;if(Navigation.SelectedIndex==14)_vm.Refresh();}
    private void Guard(Action action){try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"Touchline",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    private void CreateCareer_Click(object sender,RoutedEventArgs e)=>Guard(_vm.CreateCareer);
    private void OpenCareer_Click(object sender,RoutedEventArgs e)=>Guard(_vm.Refresh);
    private void AddCharacter_Click(object sender,RoutedEventArgs e)=>Guard(_vm.AddCharacter);
    private async void ProcessMatch_Click(object sender,RoutedEventArgs e){try{_vm.ProcessMatch();if(_vm.HasActiveInterview){_vm.MarkActiveInterviewRead();Navigation.SelectedIndex=9;}await _vm.RunAutomaticGenerationAsync();}catch(Exception ex){MessageBox.Show(ex.Message,"Touchline",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    private async void SendMessage_Click(object sender,RoutedEventArgs e){try{await _vm.SendMessageAsync();}catch(Exception ex){MessageBox.Show(ex.Message,"Touchline",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    private async void RecordPress_Click(object sender,RoutedEventArgs e){try{await _vm.RecordPressAsync();}catch(Exception ex){MessageBox.Show(ex.Message,"Post-match interview",MessageBoxButton.OK,MessageBoxImage.Warning);}}
    private void DeclineInterview_Click(object sender,RoutedEventArgs e)=>Guard(_vm.DeclineInterview);
    private async void SaveSettings_Click(object sender,RoutedEventArgs e){Guard(()=>_vm.SaveSettings(ApiKeyBox.Password));RestartFifaWatcher();await _vm.RunAutomaticGenerationAsync();}
    private void Backup_Click(object sender,RoutedEventArgs e){var dialog=new SaveFileDialog{Filter="Touchline backup (*.db)|*.db",FileName=$"touchline-backup-{DateTime.Now:yyyyMMdd-HHmm}.db"};if(dialog.ShowDialog()==true)Guard(()=>_vm.Backup(dialog.FileName));}
    private void EditProfile_Click(object sender,RoutedEventArgs e){if(_vm.SelectedCharacter is null){MessageBox.Show("Select a character in Squad first.","Touchline");return;}var dialog=new ProfileEditorWindow(_vm.SelectedCharacter){Owner=this};if(dialog.ShowDialog()==true){Guard(()=>dialog.Save());_vm.Refresh();}}
    private void Restore_Click(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Filter="Touchline backup (*.db)|*.db"};if(dialog.ShowDialog()!=true)return;if(MessageBox.Show("Restore this backup? Current local data will be replaced. Export a backup first if needed.","Restore backup",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes)Guard(()=>_vm.Restore(dialog.FileName));}
    private async void ScanFifa_Click(object sender,RoutedEventArgs e)=>await ScanFifaAsync(false);
    private void AutoFifaWatch_Click(object sender,RoutedEventArgs e){_vm.SaveSettings(null);RestartFifaWatcher();}
    private void FifaPreference_Click(object sender,RoutedEventArgs e)=>_vm.SaveSettings(null);
    private async Task ScanFifaAsync(bool automatic)
    {
        if(_fifaScanBusy){_fifaRescanRequested=true;if(!automatic)_fifaManualRescanRequested=true;return;}_fifaScanBusy=true;
        try
        {
            var result=await _vm.ScanFifaSaveAsync(automatic);
            _fifaWatcherRecoveryAttempt=0;
            if(automatic&&(result.Disposition is Fifa18ScanDisposition.CareerMismatch or Fifa18ScanDisposition.NoCareerSelected)&&_vm.CanAutoLinkPendingFifa)
            {
                _vm.CreateCareerFromPendingFifa();Navigation.SelectedIndex=1;
            }
            else if(!automatic&&(result.Disposition is Fifa18ScanDisposition.CareerMismatch or Fifa18ScanDisposition.NoCareerSelected))
            {
                var answer=MessageBox.Show($"The FIFA save belongs to {result.Parsed.PlayerName} at {result.Parsed.ClubName}. Create and link a companion career for it?","Link FIFA career",MessageBoxButton.YesNo,MessageBoxImage.Question);
                if(answer==MessageBoxResult.Yes){_vm.CreateCareerFromPendingFifa();Navigation.SelectedIndex=3;}
            }
            else if(result.Disposition==Fifa18ScanDisposition.MatchDetected&&!automatic)Navigation.SelectedIndex=3;
            await _vm.RunAutomaticGenerationAsync();
        }
        catch(Exception ex){_vm.MarkFifaSyncFailure(ex.Message);if(!automatic)MessageBox.Show(ex.Message,"FIFA 18 sync",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{_fifaScanBusy=false;if(_fifaRescanRequested){var nextAutomatic=!_fifaManualRescanRequested;_fifaRescanRequested=false;_fifaManualRescanRequested=false;if(!nextAutomatic||_vm.AutoFifaWatch)_ = Dispatcher.InvokeAsync(async()=>await ScanFifaAsync(nextAutomatic));}}
    }
    private void ReviewFifaMatch_Click(object sender,RoutedEventArgs e)=>Navigation.SelectedIndex=3;
    private void OpenSelectedReview_Click(object sender,RoutedEventArgs e)=>Guard(()=>{_vm.PopulatePendingMatchForReview();Navigation.SelectedIndex=5;});
    private void DismissSelectedReview_Click(object sender,RoutedEventArgs e){if(MessageBox.Show("Dismiss this detected match? It will not be offered again.","Dismiss match",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)Guard(_vm.DismissSelectedMatchReview);}
    private void TalkTeammate_Click(object sender,RoutedEventArgs e){if(_vm.PrepareConversation(CareerCompanion.Core.Domain.CharacterType.Teammate,CareerCompanion.Core.Domain.SceneType.PreMatch))Navigation.SelectedIndex=7;else MessageBox.Show("No active teammate is available.","Touchline");}
    private void TalkManager_Click(object sender,RoutedEventArgs e){if(_vm.PrepareConversation(CareerCompanion.Core.Domain.CharacterType.Manager,CareerCompanion.Core.Domain.SceneType.ManagerOffice))Navigation.SelectedIndex=7;else MessageBox.Show("Add a manager character first.","Touchline");}
    private void TalkAgent_Click(object sender,RoutedEventArgs e){if(_vm.PrepareAgentConversation())Navigation.SelectedIndex=7;else MessageBox.Show("No agent is available in this FIFA career.","Touchline");}
    private void OpenPress_Click(object sender,RoutedEventArgs e){_vm.MarkActiveInterviewRead();Navigation.SelectedIndex=9;}
    private void MarkWorldRead_Click(object sender,RoutedEventArgs e)=>_vm.MarkAllNotificationsRead();
    private void OpenWorldUpdate_Click(object sender,RoutedEventArgs e)=>Guard(()=>{var action=_vm.OpenSelectedNotification();Navigation.SelectedIndex=action switch{"Review"=>3,"Messages"=>7,"Press"=>9,"News"=>10,"Social"=>11,"Timeline"=>12,_=>0};});
    private void RestartFifaWatcher()
    {
        _fifaWatcher?.Dispose();_fifaWatcher=null;if(!_vm.AutoFifaWatch||!Directory.Exists(_vm.FifaSettingsDirectory))return;
        _fifaWatcher=new Fifa18SaveWatcher(_vm.FifaSettingsDirectory);_fifaWatcher.SaveChanged+=(_,_)=>Dispatcher.InvokeAsync(async()=>await ScanFifaAsync(true));_fifaWatcher.WatcherError+=(_,_)=>Dispatcher.InvokeAsync(async()=>{var delay=Math.Min(30,1<<Math.Min(5,_fifaWatcherRecoveryAttempt++));await Task.Delay(TimeSpan.FromSeconds(delay));if(!_vm.AutoFifaWatch)return;try{RestartFifaWatcher();await ScanFifaAsync(true);}catch(Exception ex){_vm.MarkFifaSyncFailure(ex.Message);}});
    }
}
