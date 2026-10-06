package uk.co.skyeye.companion.nativebridge;

import android.graphics.Bitmap;

/** Owns a single raw bitmap and its immutable matched receipt. */
public final class DecodedFrameSnapshot implements java.io.Closeable {
    private Bitmap bitmap;
    private final String receipt;
    DecodedFrameSnapshot(Bitmap bitmap,String receipt){this.bitmap=bitmap;this.receipt=receipt;}
    public String metadata(){return receipt;}
    public Bitmap takeBitmap(){Bitmap owned=bitmap;bitmap=null;return owned;}
    public void close(){if(bitmap!=null){bitmap.recycle();bitmap=null;}}
}
