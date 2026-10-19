using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace FullAIon2Companion;
public delegate void FrameHandler(ReadOnlySpan<byte> frame);
public static class Frames
{
    /// <summary>Calls the handler for the frame and, when it is a 0xFFFF bundle, for every frame inside its LZ4 block.</summary>
    public static void Walk(ReadOnlySpan<byte> frame,FrameHandler handler)
    {
        if(frame.Length<2)return;
        handler(frame);
        if(BinaryPrimitives.ReadUInt16BigEndian(frame)!=0xffff||frame.Length<7)return;
        int size=BinaryPrimitives.ReadInt32LittleEndian(frame[2..]);
        if(!Aion2Lz4.TryDecompress(frame[6..],size,out var raw))return;
        int p=0;
        while(p<raw.Length)
        {
            int at=p;long length=0;int shift=0;bool complete=false;
            while(p<raw.Length&&shift<=28){byte b=raw[p++];length|=(long)(b&127)<<shift;if((b&128)==0){complete=true;break;}shift+=7;}
            int prefix=p-at;long total=length+prefix-4;
            if(!complete||total<prefix+2||at+total>raw.Length)break;
            var sub=raw.AsSpan(p,(int)total-prefix);
            if(sub[0]!=255||sub[1]!=255)Walk(sub,handler);
            p=at+(int)total;
        }
    }
}
public sealed class FrameAnalysis
{
    readonly Dictionary<int,int> counts=[];
    readonly List<object> matches=[];
    public void Add(ReadOnlySpan<byte> frame)=>Frames.Walk(frame,Count);
    void Count(ReadOnlySpan<byte> frame)
    {
        int opcode=BinaryPrimitives.ReadUInt16BigEndian(frame);
        counts[opcode]=counts.GetValueOrDefault(opcode)+1;
        int p=frame.IndexOf("Darkni"u8);
        if(p>=0&&matches.Count<30)
            matches.Add(new{opcode=$"0x{opcode:X4}",length=frame.Length,nameAt=p,prefixHex=Convert.ToHexString(frame[..Math.Min(p+12,frame.Length)])});
    }
    public object Result()=>new{opcodes=counts.OrderByDescending(k=>k.Value).Select(k=>new{opcode=$"0x{k.Key:X4}",count=k.Value}),matches};
}
