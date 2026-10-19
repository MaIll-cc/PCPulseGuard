using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Direct3D 11 compute works across compatible NVIDIA, AMD and Intel hardware.
// COM slots are taken from Microsoft's ID3D11Device/Context definitions in d3d11.h.
public sealed class GpuPulse:IDisposable {
    [DllImport("d3d11.dll",CallingConvention=CallingConvention.StdCall)]static extern int D3D11CreateDevice(IntPtr adapter,uint driver,IntPtr software,uint flags,uint[] levels,uint levelCount,uint sdk,out IntPtr device,out uint chosen,out IntPtr context);
    [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall)]static extern int D3DCompile(byte[] source,UIntPtr length,string name,IntPtr defines,IntPtr include,string entry,string target,uint flags,uint flags2,out IntPtr code,out IntPtr error);
    [StructLayout(LayoutKind.Sequential)]struct BufferDesc{public uint Bytes,Usage,Bind,Access,Misc,Stride;}
    [StructLayout(LayoutKind.Sequential)]struct QueryDesc{public uint Query,Misc;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateBuffer(IntPtr self,ref BufferDesc desc,IntPtr initial,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateUav(IntPtr self,IntPtr resource,IntPtr desc,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateShader(IntPtr self,IntPtr data,UIntPtr length,IntPtr linkage,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateQuery(IntPtr self,ref QueryDesc desc,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate IntPtr BlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate UIntPtr BlobSize(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void BindShader(IntPtr self,IntPtr shader,IntPtr instances,uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void BindUav(IntPtr self,uint start,uint count,IntPtr[] uavs,IntPtr initialCounts);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Dispatch(IntPtr self,uint x,uint y,uint z);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void End(IntPtr self,IntPtr query);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetData(IntPtr self,IntPtr query,IntPtr data,uint bytes,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Flush(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void CopyResource(IntPtr self,IntPtr target,IntPtr source);
    [StructLayout(LayoutKind.Sequential)]struct Mapped{public IntPtr Data;public uint RowPitch,DepthPitch;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int MapResource(IntPtr self,IntPtr resource,uint subresource,uint mode,uint flags,out Mapped mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void UnmapResource(IntPtr self,IntPtr resource,uint subresource);
    IntPtr device,context,buffer,uav,shader,query,data;
    Dispatch dispatch;End end;GetData getData;Flush flush;
    static T Method<T>(IntPtr obj,int slot)where T:class{return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size),typeof(T)) as T;}
    static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    public GpuPulse() {
        try {
            uint level;Check(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0,new uint[]{0xb000},1,7,out device,out level,out context));
            string source="RWStructuredBuffer<uint> output : register(u0); [numthreads(64,1,1)] void main(uint3 id:SV_DispatchThreadID){uint x=id.x+1;[loop] for(uint i=0;i<4096;i++){x=x*1664525u+1013904223u;} output[id.x]=x;}";
            byte[] bytes=Encoding.ASCII.GetBytes(source);IntPtr code=IntPtr.Zero,error=IntPtr.Zero;
            try {
                int hr=D3DCompile(bytes,(UIntPtr)bytes.Length,AppInfo.Name,IntPtr.Zero,IntPtr.Zero,"main","cs_5_0",0x8000,0,out code,out error);
                if(hr<0)throw new InvalidOperationException("GPU shader compilation failed");
                Check(Method<CreateShader>(device,18)(device,Method<BlobPointer>(code,3)(code),Method<BlobSize>(code,4)(code),IntPtr.Zero,out shader));
            }finally{if(code!=IntPtr.Zero)Marshal.Release(code);if(error!=IntPtr.Zero)Marshal.Release(error);}
            var desc=new BufferDesc{Bytes=65536,Usage=0,Bind=0x80,Misc=0x40,Stride=4};Check(Method<CreateBuffer>(device,3)(device,ref desc,IntPtr.Zero,out buffer));
            Check(Method<CreateUav>(device,8)(device,buffer,IntPtr.Zero,out uav));
            var q=new QueryDesc{Query=0};Check(Method<CreateQuery>(device,24)(device,ref q,out query));
            Method<BindShader>(context,69)(context,shader,IntPtr.Zero,0);Method<BindUav>(context,68)(context,0,1,new IntPtr[]{uav},IntPtr.Zero);
            dispatch=Method<Dispatch>(context,41);end=Method<End>(context,28);getData=Method<GetData>(context,29);flush=Method<Flush>(context,111);data=Marshal.AllocHGlobal(4);
        }catch{Dispose();throw;}
    }
    public long Run(int percent,ManualResetEvent stop) {
        long batches=0;var clock=Stopwatch.StartNew();
        while(!stop.WaitOne(0)) {
            double start=clock.Elapsed.TotalMilliseconds,busy=500*percent/100.0;
            do {
                dispatch(context,256,1,1);end(context,query);flush(context);var timeout=Stopwatch.StartNew();
                while(true){int hr=getData(context,query,data,4,0);Check(hr);if(hr==0)break;if(timeout.Elapsed.TotalSeconds>2)throw new TimeoutException("GPU batch did not finish in time");Thread.Yield();}
                batches++;
            }while(clock.Elapsed.TotalMilliseconds-start<busy&&!stop.WaitOne(0));
            int rest=(int)Math.Max(0,500-(clock.Elapsed.TotalMilliseconds-start));if(rest>0)stop.WaitOne(rest);
        }
        if(batches>0)Verify();return batches;
    }
    void Verify() {
        IntPtr staging=IntPtr.Zero;
        try {
            var desc=new BufferDesc{Bytes=65536,Usage=3,Access=0x20000};Check(Method<CreateBuffer>(device,3)(device,ref desc,IntPtr.Zero,out staging));
            Method<CopyResource>(context,47)(context,staging,buffer);Mapped mapped;Check(Method<MapResource>(context,14)(context,staging,0,1,0,out mapped));
            try {for(uint i=0;i<32;i++){uint expected=i+1;for(int j=0;j<4096;j++)expected=unchecked(expected*1664525u+1013904223u);if(unchecked((uint)Marshal.ReadInt32(mapped.Data,(int)i*4))!=expected)throw new Exception("GPU compute verification mismatch");}}
            finally{Method<UnmapResource>(context,15)(context,staging,0);}
        }finally{if(staging!=IntPtr.Zero)Marshal.Release(staging);}
    }
    public void Dispose(){if(data!=IntPtr.Zero){Marshal.FreeHGlobal(data);data=IntPtr.Zero;}foreach(IntPtr p in new IntPtr[]{query,uav,buffer,shader,context,device})if(p!=IntPtr.Zero)Marshal.Release(p);query=uav=buffer=shader=context=device=IntPtr.Zero;}
}
