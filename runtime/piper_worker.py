"""Local stdin/stdout protocol. Text and generated audio never leave this process tree."""
import base64
import io
import json
import os
import sys
import traceback
import wave
from piper import PiperVoice, SynthesisConfig

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
voice = None
model = None


class MissingSpeechError(Exception):
    pass


for line in sys.stdin:
    stage = "request"
    try:
        request = json.loads(line)
        if request["model"] != model:
            voice = None  # Release the previous ONNX session before loading another voice.
            model = None
            next_model = request["model"]
            stage = "model"
            voice = PiperVoice.load(next_model, use_cuda=False)
            model = next_model  # Only cache a successful load; a failed model can be retried.
        stage = "synthesis"
        buffer = io.BytesIO()
        text = request["text"]
        config = SynthesisConfig(length_scale=1.0 / request["speed"])
        with wave.open(buffer, "wb") as output:
            sample_rate = voice.config.sample_rate
            output.setparams((1, 2, sample_rate, 0, "NONE", "not compressed"))
            for chunk in voice.synthesize(text, syn_config=config):
                output.writeframesraw(chunk.audio_int16_bytes)
            if output.getnframes() == 0:
                if any(char.isalnum() for char in text):
                    raise MissingSpeechError("No audio for a segment containing letters or numbers.")
                # A separator without phonemes is a short pause, still in its original position.
                output.writeframesraw(bytes(2 * (sample_rate // 10)))
        print(json.dumps({"audio": base64.b64encode(buffer.getvalue()).decode("ascii")}), flush=True)
    except Exception as error:
        # Exception messages from dependencies can include request data or local paths.
        message = "No audio for a segment containing letters or numbers." if isinstance(error, MissingSpeechError) else None
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({"error": stage, "type": type(error).__name__, "message": message,
                          "frames": [f"{os.path.basename(frame.filename)}:{frame.name}:{frame.lineno}"
                                     for frame in frames[-12:]]}), flush=True)
