using System.Collections.Immutable;
using SkyEye.GameState;

namespace SkyEye.Core.Application;

public sealed record MatchPlayerView(string Id, string Name, int Score, string Group);
public sealed record MatchFrameView(string Id, int Number, string? WinnerId, string Result, bool Current);
public sealed record MatchArchiveView(string Id, string Score, string Outcome, int Target, int RecordedFrames, int TotalFrames, DateTimeOffset At);
public sealed record MatchView(long Version, string? Id, string? FrameId, int Target, ImmutableArray<MatchPlayerView> Players,
    ImmutableArray<MatchFrameView> Frames, ImmutableArray<MatchArchiveView> Archives, string? CurrentPlayer,
    bool? Foul, string? Winner, bool Finished, Guid? UndoInputId)
{
    public static MatchView Empty { get; } = new(0, null, null, 0, [], [], [], null, null, null, false, null);
    public string Score => Players.Length == 2 ? $"{Players[0].Name} {Players[0].Score}–{Players[1].Score} {Players[1].Name}" : "";
    public string Status => Winner is not null ? $"{Name(Winner)} wins the match" : Finished ? "Match ended"
        : Frames.LastOrDefault() is { WinnerId: not null } frame ? $"{frame.Result} · frame {frame.Number}"
        : CurrentPlayer is not null ? $"{Name(CurrentPlayer)}'s turn · {Players.Single(p => p.Id == CurrentPlayer).Group}" : "Choose who starts the first frame";
    public bool CanStartFrame => Id is not null && !Finished && Winner is null && (FrameId is null || Frames.LastOrDefault()?.WinnerId is not null);
    public bool PlayingFrame => Id is not null && FrameId is not null && !Finished && Winner is null && Frames.Last().WinnerId is null;
    public bool CanStartMatch => Id is null || Finished || Winner is not null;
    public string Name(string? id) => Players.FirstOrDefault(p => p.Id == id)?.Name ?? "Unknown player";
    public static MatchView From(GameSession session)
    {
        var state = session.State;
        var archives = state.Archives.Select(a => new MatchArchiveView(a.Match.Id,
            $"{a.Match.Players[0].Name} {a.Match.Score(a.Match.Players[0].Id)}–{a.Match.Score(a.Match.Players[1].Id)} {a.Match.Players[1].Name}",
            a.Match.Winner is { } winner ? $"{a.Match.Players.Single(p => p.Id == winner).Name} won" : "Ended before a match winner was declared",
            a.Match.Target, a.Match.FrameWinners.Count, a.Match.Frames.Length, a.Match.FinishedAt ?? a.ArchivedAt)).Reverse().ToImmutableArray();
        if (state.Match is not { } match) return Empty with { Version = state.Version, Archives = archives };
        var undone = session.History.Where(e => e.Event is DecisionUndoneEvent).Select(e => ((DecisionUndoneEvent)e.Event).InputId).ToHashSet();
        var undo = session.History.LastOrDefault(e => e.MatchId == match.Id && !undone.Contains(e.InputId) && GameSession.CanUndo(e));
        return new(state.Version, match.Id, state.Frame?.Id, match.Target,
            match.Players.Select(p => new MatchPlayerView(p.Id, p.Name, match.Score(p.Id),
                state.Frame?.Colours.TryGetValue(p.Id, out var colour) == true ? colour.Value == BallColour.Red ? "reds" : "yellows" : "open table")).ToImmutableArray(),
            match.Frames.Select(f => new MatchFrameView(f.Id, f.Number, f.Winner,
                f.Winner is not null ? $"{match.Players.Single(p => p.Id == f.Winner).Name} won" : f.Closed ? "Result pending" : "In progress", f.Id == state.Frame?.Id)).ToImmutableArray(),
            archives, state.Frame?.CurrentPlayer.Value, state.Frame?.Foul?.Value, match.Winner, match.Finished, undo?.InputId);
    }
}
