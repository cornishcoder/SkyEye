using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SkyEye.Observation;

public sealed record O09ExportSession(string Session, string SplitGroupId, string Role);
public sealed record O09ExportSplitGroup(string SplitGroupId, string Role, ImmutableArray<string> Sessions,
    ImmutableArray<string> SetupGroups,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<string>? LightingStrata = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImmutableArray<string>? LayoutIds = null);
public sealed record O09DevelopmentSplitPlan(ImmutableArray<O09ExportSession> SessionAssignments,
    ImmutableArray<O09ExportSplitGroup> SplitGroups);
public sealed record O09PlannedSetupGroup(string SetupGroupId, string Role, string? LightingStratum, string? LayoutId);
public sealed record O09PredeclaredDevelopmentSplitPlan(int SchemaVersion, string CorpusId,
    ImmutableArray<string> LightingStrata, ImmutableArray<O09PlannedSetupGroup> SetupGroups);
public sealed record O09SplitPlanCoverageCell(string LightingStratum, string Role, int SetupGroups, int Layouts);
public sealed record O09PredeclaredSplitPlanAudit(string CorpusId, int SetupGroupCount, int LayoutCount,
    ImmutableArray<O09SplitPlanCoverageCell> Coverage);
public sealed record O09ExportSample(string SequenceId, string Session, string Split, string SplitGroupId, string SampleId,
    string ImageFile, string PixelSha256, string LabelFile, int VisibleBoxes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LightingStratum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LayoutId = null);
public sealed record O09TrainingExportReport(int SchemaVersion, string CorpusId, string ManifestSha256,
    string SourceSplit, ImmutableArray<O09ExportSession> SessionAssignments,
    ImmutableArray<O09ExportSplitGroup> SplitGroups,
    ImmutableArray<O09ExportSample> Samples, ImmutableArray<string> Classes, string AugmentationPolicy,
    string HoldoutPolicy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PredeclaredSplitPlanSha256 = null);

/// <summary>Exports reviewed development labels for a YOLO-compatible trainer without copying O01 holdout pixels.</summary>
public static class O09YoloDatasetExport
{
    static readonly ImmutableArray<string> Classes = ["cue", "black", "red", "yellow", "unknown"];
    sealed record PreparedSample(string SequenceId, string Session, string Role, string SplitGroupId, string SampleId,
        IndexedFrame Frame, string ImageFile, string LabelFile, string LabelText, int VisibleBoxes,
        string? LightingStratum, string? LayoutId);

    public static O09DevelopmentSplitPlan PlanDevelopmentSplit(CorpusManifest manifest,
        O09PredeclaredDevelopmentSplitPlan? predeclared = null)
    {
        var development = manifest.Sequences.Where(s => s.Kind == CorpusKind.Real && s.Split == CorpusSplit.Development)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (development.Length == 0) throw new InvalidDataException("No real Development sequences are available for O09 training export.");
        var sessions = development.Select(s => s.Session).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (sessions.Length < 3)
            throw new InvalidDataException("O09 requires at least three independent Development recording sessions; O01 Holdout sessions are never exported.");

        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string SessionNode(string id) => "session:" + id;
        string SetupNode(string id) => "setup:" + id;
        void Add(string node) { if (!parent.ContainsKey(node)) parent.Add(node, node); }
        string Find(string node)
        {
            var current = parent[node];
            if (!StringComparer.Ordinal.Equals(current, node)) parent[node] = Find(current);
            return parent[node];
        }
        void Union(string first, string second)
        {
            var a = Find(first); var b = Find(second);
            if (!StringComparer.Ordinal.Equals(a, b)) parent[b] = a;
        }

        foreach (var sequence in development)
        {
            var session = SessionNode(sequence.Session);
            Add(session);
            foreach (var setupGroup in CorpusSetupGroups.ForSequence(sequence))
            {
                var setup = SetupNode(setupGroup);
                Add(setup);
                Union(session, setup);
            }
        }

        var components = development.GroupBy(sequence => Find(SessionNode(sequence.Session)), StringComparer.Ordinal)
            .Select(component =>
            {
                var componentSequences = component.ToArray();
                var componentSessions = componentSequences.Select(s => s.Session).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToImmutableArray();
                var setupGroups = componentSequences.SelectMany(sequence => CorpusSetupGroups.ForSequence(sequence).AsEnumerable())
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
                string identity = "sessions\n" + string.Join('\n', componentSessions) + "\nsetup-groups\n" + string.Join('\n', setupGroups);
                string splitGroupId = "split-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
                    .ToLowerInvariant()[..16];
                return (Id: splitGroupId, Sessions: componentSessions, SetupGroups: setupGroups);
            }).OrderBy(component => component.Id, StringComparer.Ordinal).ToArray();
        if (components.Length < 3)
            throw new InvalidDataException("O09 needs at least three independent Development split groups after joining every shared recording session and setup/control group.");

        ImmutableArray<O09ExportSplitGroup> groups;
        if (predeclared is null)
        {
            int validationCount = Math.Clamp((int)Math.Round(components.Length * .2, MidpointRounding.AwayFromZero), 1, components.Length - 2);
            var validation = components.OrderBy(component => StableSplitGroupHash(manifest.Id,
                    component.Id, component.Sessions, component.SetupGroups), StringComparer.Ordinal)
                .ThenBy(component => component.Id, StringComparer.Ordinal).Take(validationCount)
                .Select(component => component.Id).ToHashSet(StringComparer.Ordinal);
            groups = components.Select(component => new O09ExportSplitGroup(component.Id,
                    validation.Contains(component.Id) ? "validation" : "train", component.Sessions, component.SetupGroups))
                .ToImmutableArray();
        }
        else
        {
            var plannedBySetupGroup = ValidatePredeclaredPlan(manifest, predeclared);
            var observedSetupGroups = components.SelectMany(component => component.SetupGroups)
                .ToHashSet(StringComparer.Ordinal);
            if (!observedSetupGroups.SetEquals(plannedBySetupGroup.Keys))
                throw new InvalidDataException("Predeclared O09 setup-group assignments must exactly match the Development corpus; missing or unexpected groups are not allowed.");
            var plannedGroups = components.Select(component =>
            {
                var assignments = component.SetupGroups.Select(setupGroup => plannedBySetupGroup[setupGroup]).ToArray();
                var roles = assignments.Select(assignment => assignment.Role).Distinct(StringComparer.Ordinal).ToArray();
                if (roles.Length != 1)
                    throw new InvalidDataException("A recording-session component bridges predeclared train and validation roles: " + component.Id);
                return new O09ExportSplitGroup(component.Id, roles[0], component.Sessions, component.SetupGroups,
                    assignments.Where(assignment => assignment.LightingStratum is not null)
                        .Select(assignment => assignment.LightingStratum!).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal).ToImmutableArray(),
                    assignments.Where(assignment => assignment.LayoutId is not null)
                        .Select(assignment => assignment.LayoutId!).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal).ToImmutableArray());
            }).ToImmutableArray();
            ValidatePlannedCoverage(predeclared, components, plannedGroups, plannedBySetupGroup);
            groups = plannedGroups;
        }
        var sessionAssignments = groups.SelectMany(group => group.Sessions.Select(session =>
                new O09ExportSession(session, group.SplitGroupId, group.Role)))
            .OrderBy(assignment => assignment.Session, StringComparer.Ordinal).ToImmutableArray();
        return new(sessionAssignments, groups);
    }

    static Dictionary<string, O09PlannedSetupGroup> ValidatePredeclaredPlan(CorpusManifest manifest,
        O09PredeclaredDevelopmentSplitPlan plan)
    {
        _ = ValidatePredeclaredPlanDocument(plan);
        if (!StringComparer.Ordinal.Equals(plan.CorpusId, manifest.Id))
            throw new InvalidDataException("Predeclared O09 split plan corpus ID does not match the Development manifest.");
        return plan.SetupGroups.ToDictionary(group => group.SetupGroupId, StringComparer.Ordinal);
    }

    public static O09PredeclaredSplitPlanAudit ValidatePredeclaredPlanDocument(O09PredeclaredDevelopmentSplitPlan plan)
    {
        if (plan.SchemaVersion != 1 || string.IsNullOrWhiteSpace(plan.CorpusId))
            throw new InvalidDataException("Predeclared O09 split plan schema or corpus ID is invalid.");
        if (plan.LightingStrata.IsDefaultOrEmpty || plan.LightingStrata.Any(string.IsNullOrWhiteSpace)
            || plan.LightingStrata.Distinct(StringComparer.Ordinal).Count() != plan.LightingStrata.Length)
            throw new InvalidDataException("Predeclared O09 split plan needs unique, named lighting strata.");
        if (plan.SetupGroups.IsDefaultOrEmpty || plan.SetupGroups.Any(group =>
                string.IsNullOrWhiteSpace(group.SetupGroupId)
                || group.Role is not ("train" or "validation")
                || (group.LightingStratum is null) != (group.LayoutId is null)
                || group.LightingStratum is { } stratum && !plan.LightingStrata.Contains(stratum, StringComparer.Ordinal)
                || group.LayoutId is { } layout && string.IsNullOrWhiteSpace(layout)))
            throw new InvalidDataException("Predeclared O09 split plan contains an invalid setup-group assignment.");
        if (plan.SetupGroups.GroupBy(group => group.SetupGroupId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("Predeclared O09 split plan repeats a setup-group assignment.");
        if (plan.SetupGroups.Any(group => group.LayoutId is null && group.Role != "train"))
            throw new InvalidDataException("Unstratified legacy setup groups may only be assigned to train.");
        var coverage = plan.SetupGroups.Where(group => group.LayoutId is not null).ToArray();
        if (coverage.GroupBy(group => group.LayoutId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("Each predeclared O09 coverage layout must have exactly one setup group.");
        var cells = plan.LightingStrata.SelectMany(lighting => new[] { "train", "validation" }.Select(role =>
        {
            var selected = coverage.Where(group => group.LightingStratum == lighting && group.Role == role).ToArray();
            if (selected.Length < 3)
                throw new InvalidDataException($"Predeclared O09 split needs at least three distinct layouts in {role}/{lighting}.");
            return new O09SplitPlanCoverageCell(lighting, role, selected.Length,
                selected.Select(group => group.LayoutId).Distinct(StringComparer.Ordinal).Count());
        })).ToImmutableArray();
        return new(plan.CorpusId, plan.SetupGroups.Length, coverage.Length, cells);
    }

    static void ValidatePlannedCoverage(O09PredeclaredDevelopmentSplitPlan plan,
        (string Id, ImmutableArray<string> Sessions, ImmutableArray<string> SetupGroups)[] components,
        ImmutableArray<O09ExportSplitGroup> groups,
        Dictionary<string, O09PlannedSetupGroup> plannedBySetupGroup)
    {
        var actualGroups = components.SelectMany(component => component.SetupGroups).ToHashSet(StringComparer.Ordinal);
        if (!actualGroups.SetEquals(plannedBySetupGroup.Keys))
            throw new InvalidDataException("Predeclared O09 setup-group assignments must exactly match the Development corpus; missing or unexpected groups are not allowed.");
        var roleByComponent = groups.ToDictionary(group => group.SplitGroupId, group => group.Role, StringComparer.Ordinal);
        var componentBySetupGroup = components.SelectMany(component => component.SetupGroups.Select(setupGroup => (setupGroup, component.Id)))
            .ToDictionary(row => row.setupGroup, row => row.Id, StringComparer.Ordinal);
        var coverage = plan.SetupGroups.Where(group => group.LayoutId is not null).ToArray();
        foreach (var lighting in plan.LightingStrata)
        foreach (var role in new[] { "train", "validation" })
        {
            var selected = coverage.Where(group => group.LightingStratum == lighting && group.Role == role).ToArray();
            var independentGroups = selected.Select(group => componentBySetupGroup[group.SetupGroupId])
                .Distinct(StringComparer.Ordinal).Count();
            if (selected.Length < 3 || independentGroups < 3)
                throw new InvalidDataException($"Predeclared O09 split needs at least three distinct layouts in three independent groups for {role}/{lighting}.");
        }
        foreach (var group in coverage)
            if (!StringComparer.Ordinal.Equals(roleByComponent[componentBySetupGroup[group.SetupGroupId]], group.Role))
                throw new InvalidDataException("A connected recording/setup component does not retain its predeclared role.");
    }

    public static O09TrainingExportReport Export(CorpusManifest manifest, FrozenCorpus frozen, CorpusFiles files,
        string outputDirectory, O09PredeclaredDevelopmentSplitPlan? predeclared = null, string? splitPlanSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Provide a new O09 dataset output directory.", nameof(outputDirectory));
        if ((predeclared is null) != (splitPlanSha256 is null)
            || splitPlanSha256 is not null && !CorpusFiles.IsHash(splitPlanSha256))
            throw new InvalidDataException("A predeclared O09 plan must be bound to its exact SHA-256 bytes.");
        var development = manifest.Sequences.Where(s => s.Kind == CorpusKind.Real && s.Split == CorpusSplit.Development)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var plan = PlanDevelopmentSplit(manifest, predeclared);
        var assignmentBySession = plan.SessionAssignments.ToDictionary(assignment => assignment.Session, StringComparer.Ordinal);
        var plannedBySetupGroup = predeclared?.SetupGroups.ToDictionary(group => group.SetupGroupId, StringComparer.Ordinal);

        var target = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("O09 export destination already exists; choose a new versioned directory.");
        var prepared = new List<PreparedSample>();
        long totalBytes = 0;
        foreach (var sequence in development)
        {
            var assignment = assignmentBySession[sequence.Session];
            var role = assignment.Role;
            var folder = role == "validation" ? "val" : "train";
            string? lightingStratum = null;
            string? layoutId = null;
            if (plannedBySetupGroup is not null)
            {
                var sequenceGroups = CorpusSetupGroups.ForSequence(sequence);
                if (sequenceGroups.Length != 1)
                    throw new InvalidDataException("A predeclared O09 sequence must belong to exactly one setup group: " + sequence.Id);
                var plannedGroup = plannedBySetupGroup[sequenceGroups[0]];
                lightingStratum = plannedGroup.LightingStratum;
                layoutId = plannedGroup.LayoutId;
            }
            var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(sequence.LabelsFile));
            if (!labels.Reviewed || labels.Frames.IsDefaultOrEmpty)
                throw new InvalidDataException("O09 export requires reviewed labels: " + sequence.Id);
            var index = files.Index(sequence.IndexFile).ToDictionary(f => f.SampleId, StringComparer.Ordinal);
            foreach (var frameLabels in labels.Frames.OrderBy(f => f.SampleId, StringComparer.Ordinal))
            {
                if (lightingStratum is not null)
                {
                    var lightingTags = (frameLabels.Tags.IsDefault ? ImmutableArray<string>.Empty : frameLabels.Tags)
                        .Where(tag => tag.StartsWith("lighting-stratum:", StringComparison.Ordinal)).ToArray();
                    if (lightingTags.Length != 1 || !StringComparer.Ordinal.Equals(lightingTags[0], "lighting-stratum:" + lightingStratum))
                        throw new InvalidDataException("Every planned-layout label frame must carry exactly its predeclared lighting-stratum tag: " + frameLabels.SampleId);
                }
                if (frameLabels.CoordinateSpace != "image-normalized")
                    throw new InvalidDataException("YOLO export requires reviewed image-normalized labels; no calibration warp is inferred: " + frameLabels.SampleId);
                if (frameLabels.Balls.Any(b => b.Visibility is LabelVisibility.Occluded or LabelVisibility.Uncertain))
                    throw new InvalidDataException("YOLO export cannot turn ambiguous/occluded labels into background; resolve them before export: " + frameLabels.SampleId);
                if (!index.TryGetValue(frameLabels.SampleId, out var indexed)
                    || indexed.Encoding is not (PixelEncoding.Jpeg or PixelEncoding.Png))
                    throw new InvalidDataException("Every exported label needs an indexed JPEG or PNG frame: " + frameLabels.SampleId);
                var visible = frameLabels.Balls.Where(b => b.Visibility == LabelVisibility.Visible).ToArray();
                if (visible.Any(b => b.RadiusX is null || b.RadiusY is null))
                    throw new InvalidDataException("Every visible training ball needs independently reviewed X/Y radii: " + frameLabels.SampleId);

                totalBytes = checked(totalBytes + new FileInfo(files.Resolve(indexed.PixelFile)).Length);
                if (totalBytes > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("O09 exported image set exceeds the 2 GiB bound.");
                string stem = CorpusFiles.Hash(Encoding.UTF8.GetBytes(sequence.Id + "\n" + frameLabels.SampleId)).ToLowerInvariant();
                string extension = indexed.Encoding == PixelEncoding.Png ? ".png" : ".jpg";
                string imageRelative = $"images/{folder}/{stem}{extension}";
                string labelRelative = $"labels/{folder}/{stem}.txt";
                var yoloLines = visible.Select(ball => YoloBox(ball)).ToArray();
                string labelText = yoloLines.Length == 0 ? string.Empty : string.Join("\n", yoloLines) + "\n";
                prepared.Add(new(sequence.Id, sequence.Session, role, assignment.SplitGroupId, frameLabels.SampleId, indexed,
                    imageRelative, labelRelative, labelText, visible.Length, lightingStratum, layoutId));
            }
        }

        Directory.CreateDirectory(Path.Combine(target, "images", "train"));
        Directory.CreateDirectory(Path.Combine(target, "images", "val"));
        Directory.CreateDirectory(Path.Combine(target, "labels", "train"));
        Directory.CreateDirectory(Path.Combine(target, "labels", "val"));
        var exported = ImmutableArray.CreateBuilder<O09ExportSample>(prepared.Count);
        foreach (var sample in prepared)
        {
            var pixels = files.Verified(sample.Frame.PixelFile, sample.Frame.PixelSha256);
            string imagePath = Path.Combine(target, sample.ImageFile.Replace('/', Path.DirectorySeparatorChar));
            string labelPath = Path.Combine(target, sample.LabelFile.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(imagePath, pixels);
            File.WriteAllText(labelPath, sample.LabelText, new UTF8Encoding(false));
            exported.Add(new(sample.SequenceId, sample.Session, sample.Role, sample.SplitGroupId, sample.SampleId,
                sample.ImageFile, sample.Frame.PixelSha256, sample.LabelFile, sample.VisibleBoxes,
                sample.LightingStratum, sample.LayoutId));
        }
        File.WriteAllText(Path.Combine(target, "dataset.yaml"),
            "path: .\ntrain: images/train\nval: images/val\nnames:\n  0: cue\n  1: black\n  2: red\n  3: yellow\n  4: unknown\n", new UTF8Encoding(false));
        var report = new O09TrainingExportReport(predeclared is null ? 2 : 3, manifest.Id, frozen.ManifestSha256, "Development",
            plan.SessionAssignments, plan.SplitGroups, exported.ToImmutable(), Classes,
            "No augmentation is applied during export. Apply exposure changes up to plus/minus two stops, brightness/gamma/contrast, warm/cool white balance, saturation, directional/partial shadows, blind-like stripes, highlights, dim/bright-room variation, motion blur, slight defocus, compression/noise, overlap and partial occlusion to train sessions only. Keep validation and O01 Holdout pixels unchanged.",
            "O01 Holdout sessions and pixels are not copied or listed as training samples; evaluate a frozen candidate against them only after model and thresholds are fixed.", splitPlanSha256);
        File.WriteAllText(Path.Combine(target, "o09-export.json"), CorpusJson.Write(report) + "\n", new UTF8Encoding(false));
        return report;
    }

    static string YoloBox(BallLabel ball)
    {
        var bounds = ball.Colour switch
        {
            "cue" => 0,
            "black" => 1,
            "red" => 2,
            "yellow" => 3,
            "unknown" => 4,
            _ => throw new InvalidDataException("Visible ball has unsupported O09 training class: " + ball.Colour)
        };
        double rx = ball.RadiusX!.Value, ry = ball.RadiusY!.Value;
        double left = Math.Clamp(ball.X - rx, 0, 1), right = Math.Clamp(ball.X + rx, 0, 1);
        double top = Math.Clamp(ball.Y - ry, 0, 1), bottom = Math.Clamp(ball.Y + ry, 0, 1);
        if (right <= left || bottom <= top) throw new InvalidDataException("Visible ball has an empty clipped training box: " + ball.Id);
        string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        return string.Join(' ', bounds.ToString(CultureInfo.InvariantCulture), N((left + right) / 2), N((top + bottom) / 2), N(right - left), N(bottom - top));
    }

    static string StableSplitGroupHash(string corpusId, string splitGroupId, ImmutableArray<string> sessions,
        ImmutableArray<string> setupGroups) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(corpusId + "\n" + splitGroupId + "\n"
            + string.Join('\n', sessions) + "\n" + string.Join('\n', setupGroups))));
}
