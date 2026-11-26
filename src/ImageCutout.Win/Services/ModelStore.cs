using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
namespace ImageCutout.Win.Services;
public sealed record ModelSpec(string Id, string Label, string File, string Md5) { public override string ToString()=>Label; }
public static class ModelStore
{
    public static readonly ModelSpec[] Models = [
        new("birefnet-general-lite", "快速 · BiRefNet Lite", "BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx", "4fab47adc4ff364be1713e97b7e66334"),
        new("birefnet-general", "精细 · BiRefNet General", "BiRefNet-general-epoch_244.onnx", "7a35a0141cbbc80de11d9c9a28f52697")];
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageCutout");
    private static string Target(ModelSpec model) => Path.Combine(Root, "models", model.Id + ".onnx");
    private sealed class Config { public Config() {} public string[] Mirrors { get; set; } = []; public Dictionary<string,string> Sha256 { get; set; } = new(); }
    private static Config ReadConfig() => JsonSerializer.Deserialize<Config>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"model-config.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new();
    private static async Task Verify(string path, ModelSpec model, Config config, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        if (config.Sha256.TryGetValue(model.Id, out var sha))
        { if (!Convert.ToHexString(await SHA256.HashDataAsync(input,token)).Equals(sha,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模型 SHA-256 校验失败"); }
        else if (!Convert.ToHexString(await MD5.HashDataAsync(input,token)).Equals(model.Md5,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模型与上游固定 MD5 不匹配");
    }
    public static async Task ImportAsync(ModelSpec model,string file,CancellationToken token)
    {
        var config=ReadConfig(); string path=Target(model); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (Path.GetFullPath(file).Equals(path,StringComparison.OrdinalIgnoreCase)) { await Verify(path,model,config,token); return; }
        string temp=path+"."+Guid.NewGuid().ToString("N")+".part";
        try { await using(var source=File.OpenRead(file)) await using(var dest=File.Create(temp)) await source.CopyToAsync(dest,token); await Verify(temp,model,config,token); File.Move(temp,path,true); }
        finally { File.Delete(temp); }
    }
    public static async Task<string> GetAsync(ModelSpec model,IProgress<string> progress,CancellationToken token)
    {
        var config=ReadConfig(); string path=Target(model); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if(File.Exists(path)) { await Verify(path,model,config,token); return path; }
        var sources=config.Mirrors.Select(x=>x.TrimEnd('/')+"/"+model.File).Append("https://github.com/danielgatis/rembg/releases/download/v0.0.0/"+model.File);
        using var client=new HttpClient { Timeout=TimeSpan.FromMinutes(20) }; Exception? error=null;
        foreach(var url in sources)
        {
            token.ThrowIfCancellationRequested(); string temp=path+"."+Guid.NewGuid().ToString("N")+".part";
            try {
                if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https") throw new InvalidDataException("模型源必须使用 HTTPS");
                using var headers=CancellationTokenSource.CreateLinkedTokenSource(token); headers.CancelAfter(TimeSpan.FromSeconds(20));
                using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,headers.Token); response.EnsureSuccessStatusCode();
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(20));
                await using(var input=await response.Content.ReadAsStreamAsync(deadline.Token)) await using(var output=File.Create(temp)) {
                    var buf=new byte[128*1024]; long total=0; int count; var clock=System.Diagnostics.Stopwatch.StartNew();
                    while((count=await input.ReadAsync(buf,deadline.Token))>0) {total+=count;if(total>2_000_000_000L)throw new InvalidDataException("模型过大"); await output.WriteAsync(buf.AsMemory(0,count),deadline.Token);if(clock.ElapsedMilliseconds>250){progress.Report($"下载模型 {total/1048576} MB");clock.Restart();}}
                }
                await Verify(temp,model,config,token); File.Move(temp,path,true);return path;
            }catch(Exception ex) when(!token.IsCancellationRequested){error=ex;}finally{File.Delete(temp);}
        }
        token.ThrowIfCancellationRequested();throw new IOException("模型下载失败，请配置国内模型镜像或手动导入 ONNX 文件。",error);
    }
}
