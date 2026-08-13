using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace CareerCompanion.App;

public partial class ProfileEditorWindow:Window
{
    private readonly Character _character;
    public ProfileEditorWindow(Character character){InitializeComponent();_character=character;NameText.Text=character.Name;FactsBox.Text=Pretty(character.FactsJson);PersonalityBox.Text=Pretty(character.PersonalityJson);CommunicationBox.Text=Pretty(character.CommunicationJson);HistoricalBox.Text=character.HistoricalNotes;PublicBox.IsChecked=character.IsPublic;}
    public void Save(){var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TouchlineCareerCompanion");var db=new Database(Path.Combine(root,"career-world.db"));db.UpdateCharacterProfile(_character.Id,FactsBox.Text,PersonalityBox.Text,CommunicationBox.Text,HistoricalBox.Text,PublicBox.IsChecked==true);}
    private void Accept_Click(object sender,RoutedEventArgs e){try{JsonDocument.Parse(FactsBox.Text).Dispose();JsonDocument.Parse(PersonalityBox.Text).Dispose();JsonDocument.Parse(CommunicationBox.Text).Dispose();DialogResult=true;}catch(JsonException ex){MessageBox.Show("Invalid JSON: "+ex.Message,"Profile editor");}}
    private void Export_Click(object sender,RoutedEventArgs e){var d=new SaveFileDialog{Filter="Character profile (*.json)|*.json",FileName=_character.Name.Replace(' ','-')+".json"};if(d.ShowDialog()==true)File.WriteAllText(d.FileName,JsonSerializer.Serialize(new{facts=JsonSerializer.Deserialize<object>(FactsBox.Text),personality=JsonSerializer.Deserialize<object>(PersonalityBox.Text),communication=JsonSerializer.Deserialize<object>(CommunicationBox.Text),historicalNotes=HistoricalBox.Text,isPublic=PublicBox.IsChecked==true},new JsonSerializerOptions{WriteIndented=true}));}
    private void Import_Click(object sender,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Character profile (*.json)|*.json"};if(d.ShowDialog()!=true)return;try{using var doc=JsonDocument.Parse(File.ReadAllText(d.FileName));var r=doc.RootElement;FactsBox.Text=Pretty(r.GetProperty("facts").GetRawText());PersonalityBox.Text=Pretty(r.GetProperty("personality").GetRawText());CommunicationBox.Text=Pretty(r.GetProperty("communication").GetRawText());HistoricalBox.Text=r.TryGetProperty("historicalNotes",out var h)?h.GetString()??"":"";PublicBox.IsChecked=r.TryGetProperty("isPublic",out var p)&&p.GetBoolean();}catch(Exception ex){MessageBox.Show("Could not import profile: "+ex.Message,"Profile editor");}}
    private static string Pretty(string json){using var doc=JsonDocument.Parse(json);return JsonSerializer.Serialize(doc.RootElement,new JsonSerializerOptions{WriteIndented=true});}
}
