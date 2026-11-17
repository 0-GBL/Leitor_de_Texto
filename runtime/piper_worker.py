"""Local stdin/stdout protocol. Text and generated audio never leave this process tree."""
import base64
import io
import json
import sys
import wave
from piper import PiperVoice, SynthesisConfig

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
voice = None
model = None
for line in sys.stdin:
    try:
        request = json.loads(line)
        if request["model"] != model:
            voice = None  # Release the previous ONNX session before loading another voice.
            next_model = request["model"]
            voice = PiperVoice.load(next_model, use_cuda=False)
            model = next_model  # Only cache a successful load; a failed model can be retried.
        buffer = io.BytesIO()
        with wave.open(buffer, "wb") as output:
            voice.synthesize_wav(request["text"], output,
                syn_config=SynthesisConfig(length_scale=1.0 / request["speed"]))
        print(json.dumps({"audio": base64.b64encode(buffer.getvalue()).decode("ascii")}), flush=True)
    except Exception as error:
        print(json.dumps({"error": str(error)}), flush=True)
