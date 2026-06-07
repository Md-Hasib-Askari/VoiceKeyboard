#!/bin/bash
# Benchmark: C# Voice Keyboard vs Python Voice Keyboard
set -e

echo "============================================"
echo "   Voice Keyboard Benchmark Suite"
echo "============================================"
echo ""

TMPDIR=$(mktemp -d)
WAV_FILE="$TMPDIR/test_audio.wav"

echo "🔴 Recording 5 seconds... SPEAK NOW!"
arecord -f S16_LE -r 16000 -c 1 -d 5 "$WAV_FILE" >/dev/null 2>&1
echo "✅ Recording done"
echo ""

FILE_SIZE=$(stat -c%s "$WAV_FILE")
echo "Audio: $FILE_SIZE bytes ($(($FILE_SIZE / 3200))ms)"
echo ""

# --- Test 1: Python faster-whisper direct ---
echo "--- Test 1: Python faster-whisper (direct) ---"
python3 -c "
from faster_whisper import WhisperModel
import time, io

start = time.time()
model = WhisperModel('small', device='cuda', compute_type='float16')
load_time = time.time() - start
print(f'  Model load:     {load_time:.2f}s')

with open('$WAV_FILE', 'rb') as f:
    wav_data = f.read()

start = time.time()
segments, info = model.transcribe(io.BytesIO(wav_data), language='en', beam_size=1, vad_filter=True)
text = ' '.join(s.text for s in segments).strip()
transcribe_time = time.time() - start
print(f'  Transcription:  {transcribe_time:.3f}s')
print(f'  Text:           {text}')
" 2>&1
echo ""

# --- Test 2: Python server (streaming + WebRTC VAD) ---
echo "--- Test 2: Python server (streaming + WebRTC VAD) ---"
python3 -c "
import subprocess, struct, time, threading, queue

result_queue = queue.Queue()

proc = subprocess.Popen(
    ['python3', '-u', 'Scripts/transcribe_server.py'],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
)

# Read stdout in background thread
def read_stdout():
    while True:
        line = proc.stdout.readline().decode().strip()
        if not line:
            break
        result_queue.put(line)

reader_thread = threading.Thread(target=read_stdout, daemon=True)
reader_thread.start()

# Wait for READY
while True:
    try:
        line = result_queue.get(timeout=30)
    except queue.Empty:
        print('  ERROR: Server did not start')
        proc.kill()
        break
    if line.startswith('READY'):
        parts = line.split('\t')
        print(f'  Server startup: {parts[1] if len(parts) > 1 else \"?\"}')
        break

# Read recorded audio
with open('$WAV_FILE', 'rb') as f:
    wav_data = f.read()
pcm_data = wav_data[44:]
frame_size = 960

# Stream frames at real-time pace (30ms each)
start = time.time()
speech_detected_at = None
speech_ended_at = None

for i in range(0, len(pcm_data), frame_size):
    frame = pcm_data[i:i+frame_size]
    if len(frame) < frame_size:
        frame = frame + b'\x00' * (frame_size - len(frame))
    proc.stdin.write(frame)
    proc.stdin.flush()
    time.sleep(0.030)  # Real-time pacing

# Send 500ms silence to trigger VAD end
for _ in range(16):
    proc.stdin.write(b'\x00' * frame_size)
    proc.stdin.flush()
    time.sleep(0.030)

# Wait for results
timeout = time.time() + 15
while time.time() < timeout:
    try:
        line = result_queue.get(timeout=1)
    except queue.Empty:
        continue

    if line == 'SPEECH_START':
        speech_detected_at = time.time()
        print(f'  VAD detected:   {(time.time() - start) * 1000:.0f}ms')
    elif line == 'SPEECH_END':
        speech_ended_at = time.time()
    elif line.startswith('RESULT'):
        total = (time.time() - start) * 1000
        after_speech = (time.time() - speech_ended_at) * 1000 if speech_ended_at else 0
        print(f'  Transcription:  {after_speech:.0f}ms (after speech ended)')
        print(f'  Total pipeline: {total:.0f}ms')
        print(f'  Text:           {line[7:]}')
        break
    elif line == 'NO_SPEECH':
        print(f'  No speech detected')
        break
    elif line.startswith('ERROR'):
        print(f'  Error: {line[6:]}')
        break

proc.kill()
proc.wait()
" 2>&1
echo ""

# --- Test 3: IPC overhead ---
echo "--- Test 3: IPC overhead (pipe latency) ---"
python3 -c "
import subprocess, time, threading, queue

proc = subprocess.Popen(
    ['python3', '-u', 'Scripts/transcribe_server.py'],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
)

result_queue = queue.Queue()
def read_stdout():
    while True:
        line = proc.stdout.readline().decode().strip()
        if not line: break
        result_queue.put(line)

threading.Thread(target=read_stdout, daemon=True).start()

while True:
    try:
        line = result_queue.get(timeout=30)
    except queue.Empty:
        break
    if line.startswith('READY'):
        break

silence_frame = b'\x00' * 960

# Warm up
for _ in range(10):
    proc.stdin.write(silence_frame)
proc.stdin.flush()

# Measure write throughput
iterations = 500
start = time.time()
for _ in range(iterations):
    proc.stdin.write(silence_frame)
proc.stdin.flush()
elapsed = time.time() - start

per_frame_ms = elapsed * 1000 / iterations
fps = iterations / elapsed
print(f'  Write per frame: {per_frame_ms:.3f}ms')
print(f'  Throughput:      {960 * fps / 1024:.0f} KB/s')
print(f'  Frame rate:      {fps:.0f} frames/s (need 33/s)')
if per_frame_ms < 1:
    print(f'  IPC overhead:    negligible ✓')

proc.kill()
proc.wait()
" 2>&1
echo ""

# --- Test 4: Transcription-only speed ---
echo "--- Test 4: Transcription speed (pre-recorded audio) ---"
python3 -c "
from faster_whisper import WhisperModel
import time, io, numpy as np

model = WhisperModel('small', device='cuda', compute_type='float16')

with open('$WAV_FILE', 'rb') as f:
    wav_data = f.read()

# Test different durations
for duration_sec in [1, 2, 3, 5]:
    # Create a clip of the desired duration
    bytes_needed = 44 + duration_sec * 32000
    clip = wav_data[:bytes_needed]

    times = []
    for run in range(3):
        start = time.time()
        segments, info = model.transcribe(io.BytesIO(clip), language='en', beam_size=1, vad_filter=False)
        text = ' '.join(s.text for s in segments).strip()
        elapsed = time.time() - start
        times.append(elapsed)

    avg = sum(times) / len(times)
    print(f'  {duration_sec}s audio: {avg*1000:.0f}ms avg ({times[0]*1000:.0f}/{times[1]*1000:.0f}/{times[2]*1000:.0f}ms)')

print(f'  Real-time factor: {5 / avg:.1f}x faster than real-time')
" 2>&1
echo ""

# --- Summary ---
echo "============================================"
echo "   Summary"
echo "============================================"
echo ""
echo "To compare end-to-end with your Python app:"
echo "  1. Run: dotnet run"
echo "  2. Press Start, speak, note the delay"
echo "  3. Run your Python app, same test"
echo ""

rm -rf "$TMPDIR"
echo "Benchmark complete!"
