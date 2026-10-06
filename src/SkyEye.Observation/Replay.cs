using System.Buffers.Binary;
using System.Collections.Immutable;

namespace SkyEye.Observation;

public enum ReplayStatus { Accepted, UnknownTime, Duplicate, Backwards, ScopeReset, GapReset, PixelIdMismatch, PairConflict }
public sealed record ReplayReceipt(string SampleId, long FrameId, long? SourceTimeUs, FrameScope Scope, string PixelSha256, ReplayStatus Status);
public sealed record ReplayReport(string Sequence, int Samples, int Processed, int Rejected, int ScopeResets, int GapResets,
    long MissingSourceFrameIds, string ResultSha256, ImmutableArray<ReplayReceipt> Receipts);

/// <summary>Deterministic receipt replay, not a tracker or a live decoded-frame adapter.</summary>
public static class FrameReplay
{
    public static ReplayReport Run(CorpusSequence sequence, CorpusFiles files, long maximumGapUs = 250000)
    {
        if (maximumGapUs <= 0) throw new ArgumentOutOfRangeException(nameof(maximumGapUs));
        var output = ImmutableArray.CreateBuilder<ReplayReceipt>();
        FrameScope? scope = null; long? pts = null; long lastId = -1; string? lastHash = null;
        int processed = 0, rejected = 0, resets = 0, gaps = 0; long missing = 0;
        foreach (var frame in files.Index(sequence.IndexFile))
        {
            CorpusValidation.ValidateFrame(frame, sequence.Source);
            var pixels = files.Verified(frame.PixelFile, frame.PixelSha256);
            PixelDimensions.Verify(frame, pixels);
            var status = ReplayStatus.Accepted;
            bool changed = scope is not null && scope != frame.Scope;
            if (frame.EmbeddedFrameId && (pixels.Length < 8 || BinaryPrimitives.ReadInt64LittleEndian(pixels) != frame.FrameId)) status = ReplayStatus.PixelIdMismatch;
            else if (!changed && scope is not null && frame.FrameId == lastId && (frame.PixelSha256 != lastHash || frame.SourceTimeUs != pts)) status = ReplayStatus.PairConflict;
            else if (!changed && scope is not null && (frame.FrameId == lastId || frame.SourceTimeUs is not null && frame.SourceTimeUs == pts)) status = ReplayStatus.Duplicate;
            else if (!changed && scope is not null && (frame.FrameId < lastId || frame.SourceTimeUs < pts)) status = ReplayStatus.Backwards;
            else if (changed) { status = ReplayStatus.ScopeReset; resets++; }
            else if (frame.SourceTimeUs is { } current && pts is { } previous && current - previous > maximumGapUs) { status = ReplayStatus.GapReset; gaps++; }
            else if (frame.SourceTimeUs is null) status = ReplayStatus.UnknownTime;
            if (status is ReplayStatus.PixelIdMismatch or ReplayStatus.PairConflict or ReplayStatus.Duplicate or ReplayStatus.Backwards) rejected++;
            else
            {
                if (!changed && scope is not null && frame.FrameId > lastId) missing += frame.FrameId - lastId - 1;
                processed++; scope = frame.Scope; pts = frame.SourceTimeUs; lastId = frame.FrameId; lastHash = frame.PixelSha256;
            }
            output.Add(new(frame.SampleId, frame.FrameId, frame.SourceTimeUs, frame.Scope, frame.PixelSha256, status));
        }
        var receipts = output.ToImmutable();
        var hash = CorpusFiles.Hash(System.Text.Encoding.UTF8.GetBytes(CorpusJson.Write(receipts)));
        return new(sequence.Id, receipts.Length, processed, rejected, resets, gaps, missing, hash, receipts);
    }
}
