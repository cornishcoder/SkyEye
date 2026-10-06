"""Controlled frame-ID video; synthetic pairing evidence, never table accuracy."""
import hashlib
import json
import pathlib
import subprocess
import sys

output = pathlib.Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
if output.exists():
    raise SystemExit("Refusing to replace an existing fixture")
width, height, fps, frames = 640, 360, 30, 240
encoder = subprocess.Popen([
    "ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "rawvideo",
    "-pix_fmt", "rgb24", "-s", f"{width}x{height}", "-r", str(fps), "-i", "pipe:0",
    "-an", "-c:v", "libx264", "-preset", "fast", "-crf", "16", "-bf", "2",
    "-g", "30", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-n", str(output)
], stdin=subprocess.PIPE)
for number in range(frames):
    row = bytearray([96, 96, 96] * width)
    for bit in range(12):
        colour = 235 if number & (1 << bit) else 16
        row[(40 + bit * 40) * 3:(80 + bit * 40) * 3] = bytes([colour] * 120)
    encoder.stdin.write(bytes(row) * height)
encoder.stdin.close()
if encoder.wait():
    raise SystemExit("Fixture encoding failed")
metadata = dict(schemaVersion=1, kind="Synthetic", width=width, height=height,
                fps=fps, frames=frames, clock="ClipMicroseconds", frameId="Round(PTS * 30 / 1000000)",
                barcode="12 LSB-first vertical blocks; sample x=60+40*bit,y=180; threshold=128",
                sha256=hashlib.sha256(output.read_bytes()).hexdigest().upper(),
                notes="H.264 with B frames; controlled source for native pixels/PTS pairing only")
transport = output.with_suffix(".ts")
subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", str(output),
                "-an", "-c:v", "copy", "-f", "mpegts", "-n", str(transport)], check=True)
packets = json.loads(subprocess.check_output(["ffprobe", "-v", "error", "-select_streams", "v:0",
    "-show_packets", "-show_entries", "packet=pts_time", "-of", "json", str(transport)]))
metadata["sourceOriginUs"] = round(min(float(p["pts_time"]) for p in packets["packets"]) * 1_000_000)
metadata["tsSha256"] = hashlib.sha256(transport.read_bytes()).hexdigest().upper()
output.with_suffix(".json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8", newline="\n")
print(json.dumps(metadata))
