using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyEye.GameState;

// Explicit metadata also works in trimmed Android releases; reflection alone
// cannot reliably discover the immutable dictionaries of generic facts.
[JsonSerializable(typeof(GameSnapshot))]
[JsonSerializable(typeof(ImmutableArray<EventEnvelope>))]
public partial class GameJsonContext : JsonSerializerContext;

public static class GameJson
{
    public static string WriteState(GameSnapshot state) => JsonSerializer.Serialize(state, GameJsonContext.Default.GameSnapshot);
    public static byte[] WriteHistory(ImmutableArray<EventEnvelope> history) => JsonSerializer.SerializeToUtf8Bytes(history, GameJsonContext.Default.ImmutableArrayEventEnvelope);
}
