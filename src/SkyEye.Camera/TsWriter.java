package uk.co.skyeye.camera.nativebridge;

import java.io.*;
import java.nio.ByteBuffer;
import java.util.Arrays;

/** Video-only MPEG-TS: PAT/PMT, AVC PES, 90kHz PTS and PCR. */
final class TsWriter implements Closeable {
    private final OutputStream out;
    private final int[] counters;
    TsWriter(File file) throws IOException { this(file,new int[8192]); }
    TsWriter(File file,int[] counters) throws IOException { this.counters=counters;out = new BufferedOutputStream(new FileOutputStream(file), 65536); tables(); }
    TsWriter(OutputStream out,int[] counters) throws IOException { this.counters=counters;this.out=out;tables(); }
    private static byte[] crc(byte[] src) {
        int value = -1;
        for (byte b : src) { value ^= (b & 255) << 24; for (int i=0;i<8;i++) value = (value << 1) ^ ((value < 0) ? 0x04c11db7 : 0); }
        byte[] result = Arrays.copyOf(src, src.length+4);
        for(int i=0;i<4;i++) result[src.length+i]=(byte)(value >>> (24-i*8)); return result;
    }
    private void section(int pid, byte[] data) throws IOException { byte[] bytes=new byte[data.length+1]; System.arraycopy(data,0,bytes,1,data.length); packets(pid,bytes,true,-1); }
    private void tables() throws IOException {
        section(0,crc(new byte[]{0, (byte)0xb0,13,0,1,(byte)0xc1,0,0,0,1,(byte)0xf0,0}));
        section(4096,crc(new byte[]{2,(byte)0xb0,18,0,1,(byte)0xc1,0,0,(byte)0xe1,0,(byte)0xf0,0,0x1b,(byte)0xe1,0,(byte)0xf0,0}));
    }
    static byte[] annex(byte[] input) throws IOException {
        if(input.length>=4 && input[0]==0 && input[1]==0 && (input[2]==1 || (input[2]==0 && input[3]==1))) return input;
        ByteArrayOutputStream out=new ByteArrayOutputStream(); int p=0;
        while(p+4<=input.length) { int n=ByteBuffer.wrap(input,p,4).getInt(); p+=4; if(n<=0||n>input.length-p) throw new IOException("Invalid AVC sample"); out.write(new byte[]{0,0,0,1});out.write(input,p,n);p+=n; }
        if(p!=input.length) throw new IOException("Truncated AVC sample"); return out.toByteArray();
    }
    void video(byte[] sample, long ptsUs, boolean key, byte[] config) throws IOException {
        if(key) tables();
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        bytes.write(new byte[]{0,0,1,(byte)0xe0,0,0,(byte)0x80,(byte)0x80,5});
        long pts=(ptsUs*90/1000)&0x1ffffffffL;
        bytes.write((int)(0x21|((pts>>>29)&14))); bytes.write((int)(pts>>>22)); bytes.write((int)(((pts>>>14)&0xfe)|1)); bytes.write((int)(pts>>>7)); bytes.write((int)(((pts<<1)&0xfe)|1));
        bytes.write(new byte[]{0,0,0,1,9,(byte)0xf0}); // access unit delimiter
        if(key && config!=null) bytes.write(config);
        bytes.write(annex(sample)); packets(256,bytes.toByteArray(),true,pts);
    }
    private void packets(int pid, byte[] bytes, boolean start, long pcr) throws IOException {
        int pos=0; boolean first=true;
        while(pos<bytes.length) {
            byte[] ts=new byte[188]; Arrays.fill(ts,(byte)255);
            ts[0]=0x47;ts[1]=(byte)((pid>>>8)|((first&&start)?0x40:0));ts[2]=(byte)pid;
            boolean clock=first&&pcr>=0; int maximum=clock?176:184;int count=Math.min(maximum,bytes.length-pos);int adaptation=184-count;
            ts[3]=(byte)((adaptation>0?0x30:0x10)|(counters[pid]++&15));int offset=4;
            if(adaptation>0) {
                ts[offset++]=(byte)(adaptation-1);
                if(adaptation>1) { ts[offset++]=(byte)(clock?0x10:0); if(clock) { ts[offset++]=(byte)(pcr>>>25);ts[offset++]=(byte)(pcr>>>17);ts[offset++]=(byte)(pcr>>>9);ts[offset++]=(byte)(pcr>>>1);ts[offset++]=(byte)(((pcr&1)<<7)|0x7e);ts[offset++]=0; } }
                offset=4+adaptation;
            }
            System.arraycopy(bytes,pos,ts,offset,count);out.write(ts);pos+=count;first=false;
        }
    }
    public void close() throws IOException { out.close(); }
}
