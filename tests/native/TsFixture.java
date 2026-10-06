package uk.co.skyeye.camera.nativebridge;
import java.io.*;
import java.nio.ByteBuffer;
import java.nio.file.*;
import java.util.*;
public final class TsFixture {
    public static void main(String[] args) throws Exception {
        byte[] source=Files.readAllBytes(Path.of(args[0]));List<byte[]> nals=new ArrayList<>();
        int start=-1,prefix=0;
        for(int i=0;i<source.length-3;i++) {
            int n=source[i]==0&&source[i+1]==0?(source[i+2]==1?3:source[i+2]==0&&source[i+3]==1?4:0):0;
            if(n>0){if(start>=0)nals.add(Arrays.copyOfRange(source,start+prefix,i));start=i;prefix=n;i+=n-1;}
        }
        if(start>=0)nals.add(Arrays.copyOfRange(source,start+prefix,source.length));
        int frame=0;ByteArrayOutputStream au=new ByteArrayOutputStream();boolean key=false;
        boolean streaming=args.length>2&&args[2].equals("stream");int[] counters=new int[8192];
        try(TsWriter output=streaming?null:new TsWriter(new File(args[1]));OutputStream raw=streaming?new FileOutputStream(args[1]):null) {
            for(byte[] nal:nals){if((nal[0]&31)==9 && au.size()>0){sample(output,raw,counters,au.toByteArray(),60000000L+frame++*1000000/30,key);au.reset();key=false;}
                if((nal[0]&31)==5)key=true;
                if(args.length>2&&args[2].equals("avcc"))au.write(ByteBuffer.allocate(4).putInt(nal.length).array());else au.write(new byte[]{0,0,0,1});au.write(nal);
            }
            if(au.size()>0)sample(output,raw,counters,au.toByteArray(),60000000L+frame++*1000000/30,key);
        }
        System.out.println("Muxed "+frame+" video frames");
    }
    static void sample(TsWriter output,OutputStream raw,int[] counters,byte[] au,long pts,boolean key)throws Exception{
        if(output!=null){output.video(au,pts,key,null);return;}
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();new TsWriter(bytes,counters).video(au,pts,key,null);raw.write(bytes.toByteArray());
    }
}
