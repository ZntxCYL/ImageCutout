using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ImageCutout.Win.Services;
namespace ImageCutout.Win;
public sealed class Job(string path):INotifyPropertyChanged
{
    public string Path {get;}=path;public string Name=>System.IO.Path.GetFileName(Path);
    public string? Output{get;set;} private string state="等待处理";
    public string State {get=>state;set{state=value;PropertyChanged?.Invoke(this,new(nameof(State)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public partial class MainWindow:Window
{
    private readonly ObservableCollection<Job> jobs=[];
    private CancellationTokenSource? cts;
    private string? outputDir;
    private bool checking;
    private bool manualCheck;
    private int previewGeneration;
    private static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".jpg",".jpeg",".png",".bmp",".tif",".tiff",".gif"};
    public MainWindow(){InitializeComponent();ThemeManager.Apply("System");Queue.ItemsSource=jobs;Models.ItemsSource=ModelStore.Models;Models.SelectedIndex=0;}
    private async void OnLoaded(object sender,RoutedEventArgs e){ThemeManager.ApplyWindowChrome(this);UpdateService.ConfirmHealthyStartup(Environment.GetCommandLineArgs());await CheckAsync(false);}
    private void Add(IEnumerable<string> paths){if(cts!=null)return;try{foreach(var path in paths){if(Directory.Exists(path)){Add(Directory.EnumerateFiles(path));continue;}if(File.Exists(path)&&Extensions.Contains(System.IO.Path.GetExtension(path))&&!jobs.Any(j=>j.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))jobs.Add(new(path));}if(Queue.SelectedIndex<0&&jobs.Count>0)Queue.SelectedIndex=0;Status.Text=$"已添加 {jobs.Count} 张图片";}catch(Exception ex){Status.Text=ex.Message;}}
    private void AddFiles(object s,RoutedEventArgs e){var d=new OpenFileDialog{Multiselect=true,Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif"};if(d.ShowDialog()==true)Add(d.FileNames);}
    private void AddFolder(object s,RoutedEventArgs e){var d=new OpenFolderDialog();if(d.ShowDialog()==true)Add([d.FolderName]);}
    private void OnDrop(object s,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)Add(paths);}
    private void Clear(object s,RoutedEventArgs e){jobs.Clear();Original.Source=null;Result.Source=null;Status.Text="队列已清空";}
    private void ChooseOutput(object s,RoutedEventArgs e){var d=new OpenFolderDialog();if(d.ShowDialog()==true){outputDir=d.FolderName;OutputText.Text="输出："+outputDir;}}
    private async void SelectionChanged(object s,SelectionChangedEventArgs e)=>await PreviewAsync();
    private async Task PreviewAsync(){int generation=++previewGeneration;Original.Source=null;Result.Source=null;if(Queue.SelectedItem is not Job j)return;try{var a=await Task.Run(()=>CutoutEngine.Load(j.Path));if(generation!=previewGeneration)return;Original.Source=a;if(j.Output is string p&&File.Exists(p)){var b=await Task.Run(()=>CutoutEngine.Load(p));if(generation==previewGeneration)Result.Source=b;}}catch(Exception ex){if(generation==previewGeneration)Status.Text=ex.Message;}}
    private void Busy(bool active){Controls.IsEnabled=!active;UpdateButton.IsEnabled=!active;CancelButton.IsEnabled=active;}
    private async void ImportModel(object s,RoutedEventArgs e){if(Models.SelectedItem is not ModelSpec m)return;var d=new OpenFileDialog{Filter="ONNX 模型|*.onnx"};if(d.ShowDialog()!=true)return;cts=new();Busy(true);try{await ModelStore.ImportAsync(m,d.FileName,cts.Token);Status.Text="模型校验通过，已导入";}catch(Exception ex){Status.Text=ex.Message;}finally{cts.Dispose();cts=null;Busy(false);}}
    private async void Start(object s,RoutedEventArgs e){if(cts!=null||jobs.Count==0||Models.SelectedItem is not ModelSpec model)return;cts=new();var token=cts.Token;bool gpu=Devices.SelectedIndex==0;Busy(true);Progress.Value=0;int success=0,failed=0;var batch=jobs.ToArray();
        try{Status.Text="检查模型文件…";var path=await ModelStore.GetAsync(model,new System.Progress<string>(v=>Status.Text=v),token);Status.Text="初始化 AI 推理引擎…";using var engine=await Task.Run(()=>new CutoutEngine(path,gpu),token);int index=0;foreach(var j in batch){token.ThrowIfCancellationRequested();j.State="处理中 · "+engine.Device;Status.Text=$"{index+1}/{batch.Length} · {engine.Device} · {j.Name}";var watch=System.Diagnostics.Stopwatch.StartNew();try{var folder=outputDir??System.IO.Path.Combine(System.IO.Path.GetDirectoryName(j.Path)!,"ImageCutout");j.Output=await Task.Run(()=>{var a=CutoutEngine.Load(j.Path);var result=engine.Run(a,token);token.ThrowIfCancellationRequested();return CutoutEngine.Save(result,j.Path,folder);},token);j.State=$"完成 · {watch.Elapsed.TotalSeconds:F1}s";success++;if(Queue.SelectedItem==j)await PreviewAsync();}catch(Exception ex){if(token.IsCancellationRequested){j.State="已取消";throw new OperationCanceledException(token);}j.State="失败："+ex.Message;failed++;}Progress.Value=++index*100.0/batch.Length;}Status.Text=$"完成 {success} 张，失败 {failed} 张 · {engine.Device}";}
        catch(OperationCanceledException){Status.Text=$"已取消 · 完成 {success} 张";}catch(Exception ex){Status.Text=ex.Message;}finally{cts.Dispose();cts=null;Busy(false);}}
    private void Cancel(object s,RoutedEventArgs e)=>cts?.Cancel();
    private void OnClosing(object? s,CancelEventArgs e){if(cts!=null){e.Cancel=true;cts.Cancel();Status.Text="正在取消，请在任务结束后关闭";}else if(checking&&manualCheck)e.Cancel=true;}
    private async void CheckUpdate(object s,RoutedEventArgs e)=>await CheckAsync(true);
    private async Task CheckAsync(bool manual){if(checking||cts!=null)return;checking=true;manualCheck=manual;if(manual)Controls.IsEnabled=false;UpdateButton.IsEnabled=false;try{using var service=UpdateService.CreateIfConfigured();if(service==null){if(manual)Status.Text="尚未配置更新源及公钥";return;}var next=await service.CheckAsync();if(cts!=null||!IsLoaded)return;if(next==null){if(manual)Status.Text="当前已是最新可用版本";return;}var dialog=new UpdateWindow(service,next){Owner=this};if(dialog.ShowDialog()==true){checking=false;Application.Current.Shutdown();}}catch(Exception ex){if(manual)Status.Text="检查更新失败："+ex.Message;}finally{checking=false;manualCheck=false;if(cts==null){Controls.IsEnabled=true;UpdateButton.IsEnabled=true;}}}
}
