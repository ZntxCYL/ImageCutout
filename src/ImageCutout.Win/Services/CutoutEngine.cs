using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
namespace ImageCutout.Win.Services;
public sealed class CutoutEngine : IDisposable
{
    private readonly InferenceSession session;
    public string Device { get; }
    public CutoutEngine(string path,bool gpu)
    {
        if(gpu) { try { session=Create(path,true);Device="DirectML";return; } catch(Exception ex) when(ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { } }
        session=Create(path,false);Device="CPU";
    }
    private static InferenceSession Create(string path,bool gpu)
    {
        using var opts=new SessionOptions{GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL,ExecutionMode=ExecutionMode.ORT_SEQUENTIAL};
        if(gpu){opts.EnableMemoryPattern=false;opts.AppendExecutionProvider_DML(0);}
        else {opts.IntraOpNumThreads=Math.Max(1,Environment.ProcessorCount-1);}
        return new InferenceSession(path,opts);
    }
    public static BitmapSource Load(string path)
    {
        using var input=File.OpenRead(path); var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var frame=decoder.Frames[0]; if((long)frame.PixelWidth*frame.PixelHeight>40_000_000) throw new InvalidDataException("单图超过 4000 万像素");
        var converted=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);converted.Freeze();return converted;
    }
    public BitmapSource Run(BitmapSource source,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();const int n=1024; int w=source.PixelWidth,h=source.PixelHeight;
        var pixels=new byte[checked(w*h*4)];source.CopyPixels(pixels,w*4,0);
        var resized=new TransformedBitmap(source,new ScaleTransform(n/(double)w,n/(double)h));
        var rgb=new byte[checked(resized.PixelWidth*resized.PixelHeight*4)];resized.CopyPixels(rgb,resized.PixelWidth*4,0);
        if(resized.PixelWidth!=n||resized.PixelHeight!=n)throw new InvalidDataException("输入缩放尺寸异常");
        var tensor=new DenseTensor<float>(new[]{1,3,n,n}); var buffer=tensor.Buffer.Span; int plane=n*n;int max=1;
        for(int i=0;i<plane;i++){max=Math.Max(max,rgb[i*4]);max=Math.Max(max,rgb[i*4+1]);max=Math.Max(max,rgb[i*4+2]);}
        for(int i=0;i<plane;i++){buffer[i]=(rgb[i*4+2]/(float)max-.485f)/.229f;buffer[plane+i]=(rgb[i*4+1]/(float)max-.456f)/.224f;buffer[2*plane+i]=(rgb[i*4]/(float)max-.406f)/.225f;}
        using var runOptions=new RunOptions();using var registration=token.Register(()=>runOptions.Terminate=true);
        using var outputs=session.Run(new[]{NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(),tensor)},session.OutputMetadata.Keys.ToArray(),runOptions);
        token.ThrowIfCancellationRequested(); var output=outputs.First().AsTensor<float>();var dims=output.Dimensions.ToArray();
        if(dims.Length!=4||dims[0]!=1||dims[1]!=1||dims[2]<=0||dims[3]<=0) throw new InvalidDataException("不支持的模型输出形状");
        int mh=dims[2],mw=dims[3];var mask=output.ToArray();float min=1,maxPred=0;
        for(int i=0;i<mask.Length;i++){float v=1f/(1f+MathF.Exp(-mask[i]));if(!float.IsFinite(v))throw new InvalidDataException("模型输出非有限值");mask[i]=v;min=Math.Min(min,v);maxPred=Math.Max(maxPred,v);}
        float range=maxPred-min; if(range>1e-6f)for(int i=0;i<mask.Length;i++)mask[i]=(mask[i]-min)/range;
        for(int y=0;y<h;y++) {token.ThrowIfCancellationRequested();float sy=Math.Clamp((y+.5f)*mh/h-.5f,0,mh-1);int y0=(int)sy,y1=Math.Min(y0+1,mh-1);float fy=sy-y0;
            for(int x=0;x<w;x++){float sx=Math.Clamp((x+.5f)*mw/w-.5f,0,mw-1);int x0=(int)sx,x1=Math.Min(x0+1,mw-1);float fx=sx-x0;
                float a=(mask[y0*mw+x0]*(1-fx)+mask[y0*mw+x1]*fx)*(1-fy)+(mask[y1*mw+x0]*(1-fx)+mask[y1*mw+x1]*fx)*fy;
                int i=(y*w+x)*4+3;pixels[i]=(byte)Math.Clamp(MathF.Round(pixels[i]*a),0,255);}}
        var result=BitmapSource.Create(w,h,source.DpiX>0?source.DpiX:96,source.DpiY>0?source.DpiY:96,PixelFormats.Bgra32,null,pixels,w*4);result.Freeze();return result;
    }
    public static string Save(BitmapSource image,string source,string directory)
    {
        Directory.CreateDirectory(directory);string name=Path.GetFileNameWithoutExtension(source)+"_cutout";
        for(int i=0;i<10000;i++) {string path=Path.Combine(directory,name+(i==0?"":$"_{i}")+".png");string temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".part");
            try {using(var stream=File.Create(temp)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(stream);} File.Move(temp,path,false);return path;}
            catch(IOException) when(File.Exists(path)) {} finally {File.Delete(temp);}}
        throw new IOException("输出文件名冲突过多");
    }
    public void Dispose()=>session.Dispose();
}
