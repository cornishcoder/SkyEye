package uk.co.skyeye.companion.nativebridge;
import android.content.Context;
import com.google.android.gms.cast.CastMediaControlIntent;
import com.google.android.gms.cast.framework.*;
import java.util.List;
public final class SkyEyeCastOptions implements OptionsProvider {
    @androidx.annotation.NonNull
    public CastOptions getCastOptions(Context context){return new CastOptions.Builder().setReceiverApplicationId(CastMediaControlIntent.DEFAULT_MEDIA_RECEIVER_APPLICATION_ID).build();}
    public List<SessionProvider> getAdditionalSessionProviders(Context context){return null;}
}
