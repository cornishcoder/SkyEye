using Microsoft.AspNetCore.Components.AI;

namespace SkyEye.Companion.Hybrid;

[ToolBlock("get_camera_state")]
public sealed partial class SkyCameraStateBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("discover_cameras")]
public sealed partial class SkyDiscoverCamerasBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("connect_camera")]
public sealed partial class SkyConnectCameraBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "camera_index")] public int? CameraIndex { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("host_live_session")]
public sealed partial class SkyHostLiveSessionBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("claim_camera_host")]
public sealed partial class SkyClaimCameraHostBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("release_camera_host")]
public sealed partial class SkyReleaseCameraHostBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("start_capture")]
public sealed partial class SkyStartCaptureBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("pause_capture")]
public sealed partial class SkyPauseCaptureBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("resume_capture")]
public sealed partial class SkyResumeCaptureBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("stop_capture")]
public sealed partial class SkyStopCaptureBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("choose_chromecast")]
public sealed partial class SkyChooseChromecastBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("cast_live_table")]
public sealed partial class SkyCastLiveTableBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("stop_cast")]
public sealed partial class SkyStopCastBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("get_table_state")]
public sealed partial class SkyTableStateBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("calibrate_table")]
public sealed partial class SkyCalibrateTableBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("apply_table_calibration")]
public sealed partial class SkyApplyTableCalibrationBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("suggest_shots")]
public sealed partial class SkySuggestShotsBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("explain_shot")]
public sealed partial class SkyExplainShotBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "shot_id")] public string? ShotId { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("select_shot_by_number")]
public sealed partial class SkySelectShotNumberBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "number")] public int Number { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("select_shot_by_id")]
public sealed partial class SkySelectShotIdBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "shot_id")] public string? ShotId { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("clear_shots")]
public sealed partial class SkyClearShotsBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("get_match_state")]
public sealed partial class SkyMatchStateBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("review_settled_table_change")]
public sealed partial class SkyReviewSettledTableChangeBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "candidate_id")] public string? CandidateId { get; set; }
    [ToolParameter(Name = "action")] public string? Action { get; set; }
    [ToolParameter(Name = "reds")] public int? Reds { get; set; }
    [ToolParameter(Name = "yellows")] public int? Yellows { get; set; }
    [ToolParameter(Name = "cue_balls")] public int? CueBalls { get; set; }
    [ToolParameter(Name = "black_balls")] public int? BlackBalls { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("start_match")]
public sealed partial class SkyStartMatchBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "first_player")] public string? FirstPlayer { get; set; }
    [ToolParameter(Name = "second_player")] public string? SecondPlayer { get; set; }
    [ToolParameter(Name = "target")] public int Target { get; set; }
    [ToolParameter(Name = "expected_game_version")] public long ExpectedGameVersion { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("start_frame")]
public sealed partial class SkyStartFrameBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "starting_player")] public string? StartingPlayer { get; set; }
    [ToolParameter(Name = "expected_game_version")] public long ExpectedGameVersion { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("finish_match")]
public sealed partial class SkyFinishMatchBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "expected_game_version")] public long ExpectedGameVersion { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("declare_match_colours")]
public sealed partial class SkyDeclareColoursBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "group")] public string? Group { get; set; }
    [ToolParameter(Name = "player_name")] public string? PlayerName { get; set; }
    [ToolParameter(Name = "expected_game_version")] public long ExpectedGameVersion { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("correct_match_state")]
public sealed partial class SkyCorrectMatchStateBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "kind")] public string? Kind { get; set; }
    [ToolParameter(Name = "player_name")] public string? PlayerName { get; set; }
    [ToolParameter(Name = "group")] public string? Group { get; set; }
    [ToolParameter(Name = "foul")] public bool? Foul { get; set; }
    [ToolParameter(Name = "ball_id")] public string? BallId { get; set; }
    [ToolParameter(Name = "presence")] public string? Presence { get; set; }
    [ToolParameter(Name = "expected_game_version")] public long ExpectedGameVersion { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("undo_match_decision")]
public sealed partial class SkyUndoMatchDecisionBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "expected_game_version")] public long ExpectedGameVersion { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("record_declared_shot")]
public sealed partial class SkyRecordDeclaredShotBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "is_break")] public bool IsBreak { get; set; }
    [ToolParameter(Name = "legality")] public string? Legality { get; set; }
    [ToolParameter(Name = "reds_potted")] public int RedsPotted { get; set; }
    [ToolParameter(Name = "yellows_potted")] public int YellowsPotted { get; set; }
    [ToolParameter(Name = "black_potted")] public bool BlackPotted { get; set; }
    [ToolParameter(Name = "cue_ball_potted")] public bool CueBallPotted { get; set; }
    [ToolParameter(Name = "cue_ball_off_table")] public bool CueBallOffTable { get; set; }
    [ToolParameter(Name = "legal_break")] public bool? LegalBreak { get; set; }
    [ToolParameter(Name = "first_contact")] public string? FirstContact { get; set; }
    [ToolParameter(Name = "call_required")] public bool? CallRequired { get; set; }
    [ToolParameter(Name = "called_group")] public string? CalledGroup { get; set; }
    [ToolParameter(Name = "reds_before")] public int? RedsBefore { get; set; }
    [ToolParameter(Name = "yellows_before")] public int? YellowsBefore { get; set; }
    [ToolParameter(Name = "replace_latest")] public bool ReplaceLatest { get; set; }
    [ToolParameter(Name = "expected_game_version")] public long ExpectedGameVersion { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("save_recent_clip")]
public sealed partial class SkySaveRecentClipBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "seconds")] public int Seconds { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("get_saved_clips")]
public sealed partial class SkySavedClipsBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("play_saved_clip")]
public sealed partial class SkyPlaySavedClipBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "clip_number")] public int ClipNumber { get; set; }
    [ToolParameter(Name = "target")] public string? Target { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("replay_recent")]
public sealed partial class SkyReplayRecentBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "seconds")] public int Seconds { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("control_tv_replay")]
public sealed partial class SkyControlTvReplayBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "action")] public string? Action { get; set; }
    [ToolParameter(Name = "seconds")] public long? Seconds { get; set; }
    [ToolResult] public string? ResultText { get; set; }
}

[ToolBlock("return_to_live")]
public sealed partial class SkyReturnToLiveBlock : FunctionInvocationContentBlock
{
    [ToolResult] public string? ResultText { get; set; }
}
