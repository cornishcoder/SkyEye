using SkyEye.Core.Application;
using G = SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    public MatchView Match => app.State.Match;
    public bool CanManageMatch => app.State.CanManageMatch;
    public string? MatchError => app.State.GameError;

    public Task<bool> StartManualMatchAsync(string firstName, string secondName, int target, long version)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var first = Guid.NewGuid().ToString("N"); var second = Guid.NewGuid().ToString("N");
            var match = Guid.NewGuid().ToString("N"); var frame = Guid.NewGuid().ToString("N");
            if (!await SendManualAsync(new G.StartMatch([new(first, firstName.Trim()), new(second, secondName.Trim())], target), version, match, frame)) return false;
            // If frame creation fails, the durable match remains visible with starter controls.
            return await SendManualAsync(new G.StartFrame(first), version + 1, match, frame);
        });
    public Task<bool> StartNextFrameAsync(long version, string player) => SendManualAsync(new G.StartFrame(player), version, Match.Id!, Guid.NewGuid().ToString("N"));
    public Task<bool> SetMatchTurnAsync(long version, string player) => SendManualAsync(new G.PlayerCorrection(new G.ChangeTurn(player)), version);
    public Task<bool> DeclareMatchColoursAsync(long version, string? player = null, G.BallColour? colour = null)
        => SendManualAsync(new G.DeclareColours(player, colour), version);
    public Task<bool> SetMatchFoulAsync(long version, bool foul) => SendManualAsync(new G.PlayerCorrection(new G.SetFoul(foul)), version);
    public Task<bool> SetFrameWinnerAsync(long version, string frame, string player) => SendManualAsync(new G.PlayerCorrection(new G.SetFrameWinner(player)), version, frame: frame);
    public Task<bool> UndoMatchDecisionAsync(long version, Guid inputId) => SendManualAsync(new G.UndoDecision(inputId), version);
    public Task<bool> FinishManualMatchAsync(long version) => SendManualAsync(new G.FinishMatch(), version);
    public Task<bool> RecordDeclaredShotAsync(long version, string match, string frame, G.DeclaredShot facts, Guid? corrects = null)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!active || disposed || EditingTable) return false;
            var receipt = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
                new DeclareShotOutcome(match, frame, version, facts, corrects)), foreground?.Token ?? CancellationToken.None);
            if (!receipt.Succeeded) ShowError(receipt.Message); else { Message = receipt.Message; Publish(); }
            return receipt.Succeeded;
        });

    Task<bool> SendManualAsync(G.GameInput input, long version, string? match = null, string? frame = null)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!active || disposed || EditingTable) { ShowError("Use the match controls when the application is active."); return false; }
            var current = app.State;
            var evidence = new G.GameInputEnvelope(Guid.NewGuid(), match ?? current.Match.Id ?? "", frame ?? current.Match.FrameId ?? "setup",
                DateTimeOffset.UtcNow, input, Provenance: G.Provenance.PlayerConfirmed);
            var result = await app.DispatchAsync(new(evidence.Id, CommandOrigin.Touch, current.Version, new SubmitGameInput(evidence, version)), foreground?.Token ?? CancellationToken.None);
            if (!result.Succeeded) ShowError(result.Message);
            else { Message = "Match updated."; Publish(); }
            return result.Succeeded;
        });
}
