using System.Text.Json;

namespace SkyEye.Observation;

/// <summary>Standalone, read-only viewer for one bounded offline O04 trace.</summary>
public static class O04AssociationHtml
{
    public const int MaximumFrames = 600;
    const long MaximumImageBytes = 64L * 1024 * 1024;

    public static string Render(O04AssociationReport report, CorpusFiles files)
    {
        var frames = report.TraceFrames;
        if (frames is null || frames.Value.IsDefaultOrEmpty)
            throw new InvalidDataException("The report has no O04 trace. Select --viewer-sequence to create one.");
        if (frames.Value.Length > MaximumFrames || frames.Value.Any(f => f.Encoding != PixelEncoding.Jpeg
            || f.CoordinateSpace != "table-2x1"))
            throw new InvalidDataException("O04 viewer frames must be bounded JPEGs with calibrated table-2x1 labels.");

        long totalBytes = 0;
        foreach (var frame in frames.Value)
        {
            var path = files.Resolve(frame.PixelFile);
            var size = new FileInfo(path).Length;
            if (size is < 1 or > 32L * 1024 * 1024)
                throw new InvalidDataException("An O04 viewer frame is empty or exceeds the 32 MiB asset bound.");
            totalBytes = checked(totalBytes + size);
            if (totalBytes > MaximumImageBytes)
                throw new InvalidDataException("O04 viewer images exceed the 64 MiB total payload bound; select a shorter sequence.");
        }

        var payloadFrames = new List<object>(frames.Value.Length);
        foreach (var frame in frames.Value)
        {
            var bytes = files.Verified(frame.PixelFile, frame.PixelSha256);
            payloadFrames.Add(new
            {
                frame.SequenceId, frame.Session, frame.SampleId, frame.FrameId, frame.SourceTimeUs,
                frame.Tags, frame.CoordinateSpace, frame.PixelSha256, frame.Width, frame.Height,
                image = "data:image/jpeg;base64," + Convert.ToBase64String(bytes),
                frame.Labels, frame.Detections, frame.Tracks
            });
        }
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, report.CorpusId, report.Split, report.ManifestSha256,
            report.CandidateName, report.DetectionSha256, report.Configuration, frames = payloadFrames
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return """
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>SkyEye O04 sequence viewer</title>
            <style>
            :root { color-scheme: dark; font: 15px/1.45 system-ui, sans-serif; background:#10151b; color:#e8edf2; }
            * { box-sizing:border-box; }
            body { margin:0; }
            header { position:sticky; top:0; z-index:2; background:#18212b; border-bottom:1px solid #354352; padding:12px 18px; }
            h1 { font-size:20px; margin:0 0 8px; }
            .controls { display:flex; align-items:center; gap:10px; flex-wrap:wrap; }
            button { border:1px solid #637487; background:#263545; color:inherit; border-radius:5px; padding:7px 12px; font:inherit; cursor:pointer; }
            button:hover { background:#33485e; }
            input[type=range] { flex:1 1 220px; min-width:120px; }
            #meta { color:#b4c0cc; font-size:13px; margin-top:6px; }
            main { padding:14px; display:grid; grid-template-columns:minmax(0, 1.25fr) minmax(420px, .9fr); gap:14px; align-items:start; }
            section { background:#18212b; border:1px solid #354352; border-radius:7px; padding:12px; min-width:0; }
            h2 { font-size:16px; margin:0 0 10px; }
            #raw { width:100%; height:auto; max-height:72vh; object-fit:contain; background:#080b0e; display:block; }
            #table { width:100%; display:block; background:#12613e; border:10px solid #573d2d; border-radius:8px; }
            #frameInfo { margin:10px 0 0; white-space:pre-wrap; color:#c4d0dc; }
            #legend { display:flex; flex-wrap:wrap; gap:12px; color:#c4d0dc; font-size:13px; margin-top:9px; }
            .dot { display:inline-block; width:12px; height:12px; border-radius:50%; margin-right:5px; vertical-align:-1px; }
            .rows { overflow:auto; max-height:72vh; }
            table { width:100%; border-collapse:collapse; font-size:13px; white-space:nowrap; }
            th,td { text-align:left; padding:6px 8px; border-bottom:1px solid #354352; }
            th { position:sticky; top:0; background:#202c38; }
            .note { color:#aebbc8; font-size:12px; margin:12px 0 0; }
            @media(max-width:950px) { main { grid-template-columns:1fr; } .rows { max-height:55vh; } }
            </style>
            </head>
            <body>
            <header>
              <h1>SkyEye O04 offline sequence viewer</h1>
              <div class="controls">
                <button id="previous" type="button">Previous</button>
                <button id="play" type="button">Play</button>
                <button id="next" type="button">Next</button>
                <input id="frame" type="range" min="0" value="0" aria-label="Frame">
                <span id="counter"></span>
              </div>
              <div id="meta"></div>
            </header>
            <main>
              <section>
                <h2>Raw camera frame</h2>
                <img id="raw" alt="Raw indexed camera frame">
                <div id="frameInfo"></div>
              </section>
              <section>
                <h2>Calibrated 2:1 table view</h2>
                <svg id="table" viewBox="0 0 2 1" role="img" aria-label="Labels, detections and tracks in calibrated table coordinates">
                  <g id="paths"></g><g id="markers"></g>
                </svg>
                <div id="legend">
                  <span><i class="dot" style="background:#d27aff"></i>Reviewed label</span>
                  <span><i class="dot" style="background:#34d6ff"></i>Current detection</span>
                  <span><i class="dot" style="background:#ffad42"></i>Unseen track prediction</span>
                </div>
                <p class="note">The table diagram uses reviewed calibrated coordinates. It is shown beside the raw image because this corpus does not include a pixel-to-table transform. Predictions are diagnostic estimates, not sightings.</p>
              </section>
              <section style="grid-column:1/-1">
                <h2>Frame candidates and association detail</h2>
                <div class="rows"><table><thead><tr>
                  <th>Detection</th><th>Colour</th><th>Status</th><th>Track</th><th>Confidence</th><th>Last seen</th><th>Prediction</th><th>Residual</th><th>Motion</th><th>Alternatives</th><th>Reviewed label</th>
                </tr></thead><tbody id="details"></tbody></table></div>
              </section>
            </main>
            <script id="trace" type="application/json">__TRACE_JSON__</script>
            <script>
            (() => {
              const data = JSON.parse(document.getElementById('trace').textContent);
              const range = document.getElementById('frame');
              const image = document.getElementById('raw');
              const svg = document.getElementById('table');
              const markerLayer = document.getElementById('markers');
              const pathLayer = document.getElementById('paths');
              const body = document.getElementById('details');
              const ns = 'http://www.w3.org/2000/svg';
              let timer = null;
              range.max = String(data.frames.length - 1);
              const c = data.configuration;
              document.getElementById('meta').textContent = `${data.corpusId} · ${data.split} · ${data.candidateName} · tolerance ${c.centreTolerance} · max balls ${c.maximumBallInventory} · move ${c.movementStartTableWidthsPerSecond}/${c.movementStopTableWidthsPerSecond} · manifest ${data.manifestSha256.slice(0, 12)} · detections ${data.detectionSha256.slice(0, 12)}`;
              function element(name, attrs, title) {
                const node = document.createElementNS(ns, name);
                for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, String(value));
                if (title) { const t = document.createElementNS(ns, 'title'); t.textContent = title; node.appendChild(t); }
                return node;
              }
              function point(node, x, y, text, colour, radius = .025) {
                node.appendChild(element('circle', {cx:x, cy:y, r:radius, fill:colour, stroke:'#0b1015', 'stroke-width':'.006'}, text));
              }
              function cell(row, value) { const td = document.createElement('td'); td.textContent = value ?? '—'; row.appendChild(td); }
              function show(index) {
                const f = data.frames[index];
                image.src = f.image;
                image.alt = `Raw frame ${f.frameId}, sample ${f.sampleId}`;
                document.getElementById('counter').textContent = `${index + 1} / ${data.frames.length}`;
                range.value = String(index);
                const tags = f.tags.length ? f.tags.join(', ') : 'none';
                document.getElementById('frameInfo').textContent = `Sequence: ${f.sequenceId}\nSession: ${f.session}\nFrame / sample: ${f.frameId} / ${f.sampleId}\nSource PTS: ${f.sourceTimeUs} µs (${(f.sourceTimeUs / 1e6).toFixed(3)} s)\nTags: ${tags}\nJPEG SHA-256: ${f.pixelSha256}`;
                markerLayer.replaceChildren(); pathLayer.replaceChildren(); body.replaceChildren();
                for (const t of f.tracks) {
                  if (t.status === 'predicted' || t.status === 'ambiguous') {
                    pathLayer.appendChild(element('line', {x1:t.lastSeenX,y1:t.lastSeenY,x2:t.predictedX,y2:t.predictedY,stroke:'#ffad42','stroke-width':'.008','stroke-dasharray':'.025 .018'}));
                    markerLayer.appendChild(element('circle', {cx:t.predictedX,cy:t.predictedY,r:'.036',fill:'none',stroke:'#ffad42','stroke-width':'.008','stroke-dasharray':'.018 .012'}, `Track ${t.trackId}, ${t.status}; last seen ${t.ageUs} µs ago at ${t.lastSeenX.toFixed(3)}, ${t.lastSeenY.toFixed(3)}`));
                    const label = element('text',{x:t.predictedX+.035,y:t.predictedY-.035,fill:'#fff0d9','font-size':'.045'},`T${t.trackId}?`); markerLayer.appendChild(label);
                  } else {
                    markerLayer.appendChild(element('circle', {cx:t.lastSeenX,cy:t.lastSeenY,r:'.034',fill:'none',stroke:t.status==='new'?'#6dff9b':'#34d6ff','stroke-width':'.007'}, `Track ${t.trackId}, ${t.status}, ${t.motion}`));
                  }
                }
                for (const l of f.labels) {
                  const color = l.visibility === 'Visible' ? '#d27aff' : '#a8a0ad';
                  markerLayer.appendChild(element('circle',{cx:l.x,cy:l.y,r:'.012',fill:color,stroke:'#fff','stroke-width':'.004'},`Reviewed ${l.visibility}: ${l.id} (${l.colour})`));
                  markerLayer.appendChild(element('text',{x:l.x+.014,y:l.y-.014,fill:'#fff','font-size':'.04'},`L:${l.id}`));
                }
                const statusColor = {matched:'#34d6ff',new:'#6dff9b',ambiguous:'#ff5d6c','inventory-limited':'#ff5d6c',untracked:'#b8c2cc'};
                for (const d of f.detections) {
                  point(markerLayer,d.x,d.y,`Detection ${d.id}; ${d.status}; ${d.colour}; confidence ${d.confidence ?? 'unknown'}`,statusColor[d.status] || '#b8c2cc',.023);
                  markerLayer.appendChild(element('text',{x:d.x+.025,y:d.y+.035,fill:'#fff','font-size':'.038'},`D:${d.id}`));
                  const row = document.createElement('tr');
                  cell(row,d.id); cell(row,d.colour); cell(row,d.status); cell(row,d.trackId === null ? '—' : `T${d.trackId}`);
                  cell(row,d.confidence === null ? '—' : Number(d.confidence).toFixed(3));
                  cell(row,d.lastSeenSourceTimeUs === null ? '—' : `${((f.sourceTimeUs-d.lastSeenSourceTimeUs)/1e6).toFixed(3)} s at ${d.lastSeenX.toFixed(3)}, ${d.lastSeenY.toFixed(3)}`);
                  cell(row,d.predictedX === null ? '—' : `${d.predictedX.toFixed(3)}, ${d.predictedY.toFixed(3)}`);
                  cell(row,d.residual === null ? '—' : Number(d.residual).toFixed(4)); cell(row,d.motion);
                  cell(row,d.alternativeTrackIds.length ? d.alternativeTrackIds.map(id=>`T${id}`).join(', ') : '—');
                  cell(row,d.evaluationTruthId ? `${d.evaluationTruthId} (evaluation)` : '—'); body.appendChild(row);
                }
                for (const t of f.tracks.filter(track => track.status === 'predicted' || track.status === 'ambiguous')) {
                  const row = document.createElement('tr');
                  cell(row,'—'); cell(row,'—'); cell(row,`${t.status} track`); cell(row,`T${t.trackId}`); cell(row,'—');
                  cell(row,`${(t.ageUs/1e6).toFixed(3)} s at ${t.lastSeenX.toFixed(3)}, ${t.lastSeenY.toFixed(3)}`);
                  cell(row,`${t.predictedX.toFixed(3)}, ${t.predictedY.toFixed(3)}`); cell(row,'—'); cell(row,t.motion);
                  cell(row,'—'); cell(row,'—'); body.appendChild(row);
                }
              }
              document.getElementById('previous').onclick = () => show(Math.max(0, Number(range.value)-1));
              document.getElementById('next').onclick = () => show(Math.min(data.frames.length-1, Number(range.value)+1));
              range.oninput = () => show(Number(range.value));
              document.getElementById('play').onclick = event => {
                if (timer) { clearInterval(timer); timer = null; event.currentTarget.textContent = 'Play'; return; }
                event.currentTarget.textContent = 'Pause';
                timer = setInterval(() => { const next = (Number(range.value)+1) % data.frames.length; show(next); }, 350);
              };
              show(0);
            })();
            </script>
            </body>
            </html>
            """.Replace("__TRACE_JSON__", json, StringComparison.Ordinal);
    }
}
