"""
Continuous real-time STT via faster-whisper.
Writes RESULT: lines to stdout as soon as each chunk is transcribed.
Usage: python -u stt.py [chunk_seconds [language]]
"""
import sys, os, tempfile
sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")

import numpy as np
import sounddevice as sd
import scipy.io.wavfile as wav

CHUNK = float(sys.argv[1]) if len(sys.argv) > 1 else 5.0
LANG  = sys.argv[2] if len(sys.argv) > 2 else "pl"
if LANG == "auto": LANG = None

SAMPLE_RATE = 16000

print("LOADING", flush=True)
from faster_whisper import WhisperModel
model = WhisperModel("small", device="cpu", compute_type="int8")
print("READY", flush=True)

def transcribe(audio: np.ndarray) -> str:
    tmp = tempfile.mktemp(suffix=".wav")
    try:
        wav.write(tmp, SAMPLE_RATE, (audio * 32767).astype(np.int16))
        segs, _ = model.transcribe(tmp, language=LANG, beam_size=3)
        return " ".join(s.text.strip() for s in segs).strip()
    finally:
        try: os.unlink(tmp)
        except: pass

while True:
    try:
        print("RECORDING", flush=True)
        audio = sd.rec(int(CHUNK * SAMPLE_RATE), samplerate=SAMPLE_RATE,
                       channels=1, dtype="float32")
        sd.wait()
        text = transcribe(audio)
        if text:
            print(f"RESULT:{text}", flush=True)
    except KeyboardInterrupt:
        break
    except Exception as ex:
        print(f"ERROR:{ex}", file=sys.stderr, flush=True)
