#!/usr/bin/env python3
"""
Real-time transcription server with WebRTC VAD.
Usage: python3 transcribe_server.py [model_name]
  model_name: tiny, tiny.en, base, small, medium, large-v3 (default: small)
"""
import sys
import struct
import io
import time
import webrtcvad

SAMPLE_RATE = 16000
FRAME_SIZE = 480
FRAME_BYTES = FRAME_SIZE * 2
SILENCE_TIMEOUT = 0.3
MIN_SPEECH_DURATION = 0.3

def main():
    model_name = sys.argv[1] if len(sys.argv) > 1 else "small"
    
    sys.stdout.reconfigure(line_buffering=True)
    
    from faster_whisper import WhisperModel

    print(f"LOADING\t{model_name}", flush=True)

    device = "cuda"
    compute_type = "float16"
    try:
        import torch
        if not torch.cuda.is_available():
            device = "cpu"
            compute_type = "int8"
    except ImportError:
        device = "cpu"
        compute_type = "int8"

    model = WhisperModel(model_name, device=device, compute_type=compute_type)
    print(f"READY\t{model_name}\t{device}/{compute_type}", flush=True)

    vad = webrtcvad.Vad(3)

    speech_buffer = []
    speech_active = False
    last_speech_time = 0.0

    while True:
        try:
            frame = sys.stdin.buffer.read(FRAME_BYTES)
            if not frame or len(frame) < FRAME_BYTES:
                break

            try:
                is_speech = vad.is_speech(frame, SAMPLE_RATE)
            except Exception:
                is_speech = False

            now = time.time()

            if is_speech:
                if not speech_active:
                    speech_active = True
                    print("SPEECH_START", flush=True)
                last_speech_time = now
                speech_buffer.append(frame)
            elif speech_active:
                speech_buffer.append(frame)
                silence_duration = now - last_speech_time
                if silence_duration >= SILENCE_TIMEOUT:
                    speech_active = False
                    print("SPEECH_END", flush=True)
                    audio_duration = len(speech_buffer) * 30 / 1000.0
                    if audio_duration >= MIN_SPEECH_DURATION:
                        pcm_data = b''.join(speech_buffer)
                        wav_data = pcm_to_wav(pcm_data, SAMPLE_RATE)
                        audio_file = io.BytesIO(wav_data)
                        segments, info = model.transcribe(
                            audio_file,
                            language="en",
                            beam_size=1,
                            vad_filter=False,
                        )
                        text = " ".join(s.text for s in segments).strip()
                        if text:
                            print(f"RESULT\t{text}", flush=True)
                        else:
                            print("NO_SPEECH", flush=True)
                    else:
                        print("NO_SPEECH", flush=True)
                    speech_buffer = []

        except Exception as e:
            print(f"ERROR\t{e}", flush=True)

    print("EXIT", flush=True)


def pcm_to_wav(pcm_data, sample_rate):
    import struct
    num_channels = 1
    bits_per_sample = 16
    byte_rate = sample_rate * num_channels * bits_per_sample // 8
    block_align = num_channels * bits_per_sample // 8
    data_size = len(pcm_data)
    wav = io.BytesIO()
    wav.write(b'RIFF')
    wav.write(struct.pack('<I', 36 + data_size))
    wav.write(b'WAVE')
    wav.write(b'fmt ')
    wav.write(struct.pack('<I', 16))
    wav.write(struct.pack('<H', 1))
    wav.write(struct.pack('<H', num_channels))
    wav.write(struct.pack('<I', sample_rate))
    wav.write(struct.pack('<I', byte_rate))
    wav.write(struct.pack('<H', block_align))
    wav.write(struct.pack('<H', bits_per_sample))
    wav.write(b'data')
    wav.write(struct.pack('<I', data_size))
    wav.write(pcm_data)
    wav.seek(0)
    return wav.read()


if __name__ == "__main__":
    main()
