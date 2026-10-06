package uk.co.skyeye.companion.nativebridge;

import java.util.ArrayDeque;

/** Metadata only: match the timestamp actually latched by SurfaceTexture.
 * Never select a timestamp by proximity or by the current wall clock. */
final class DecodedFrameLedger {
    static final int CAPACITY = 64;
    static final class Stamp {
        final long generation, frameId, presentationUs, releaseNs;
        final int width, height;
        Stamp(long generation,long id,long pts,long release,int width,int height) {
            this.generation=generation;frameId=id;presentationUs=pts;releaseNs=release;this.width=width;this.height=height;
        }
    }
    private final ArrayDeque<Stamp> frames = new ArrayDeque<>();
    private long generation, nextId;
    synchronized void reset(long generation) { this.generation=generation;nextId=0;frames.clear(); }
    synchronized void frame(long generation,long pts,long release,int width,int height) {
        if(generation!=this.generation||pts<0||release<=0||width<=0||height<=0||width>3840||height>2160)return;
        frames.addLast(new Stamp(generation,++nextId,pts,release,width,height));
        while(frames.size()>CAPACITY)frames.removeFirst();
    }
    synchronized Stamp match(long generation,long surfaceNs) {
        if(generation!=this.generation||surfaceNs<=0)return null;
        Stamp result=null;
        for(Stamp frame:frames)if(frame.releaseNs==surfaceNs) {
            if(result!=null)return null; // A colliding surface timestamp is ambiguous.
            result=frame;
        }
        return result;
    }
    synchronized int size() { return frames.size(); }
    synchronized long newestRelease() { return frames.isEmpty()?0:frames.getLast().releaseNs; }
}
