"""Run as a file: Windows PowerShell 5.1 strips quotes from native -c arguments."""
from importlib.metadata import version
from piper import PiperVoice, SynthesisConfig
import onnxruntime

assert callable(PiperVoice.load)
assert SynthesisConfig(length_scale=1.0).length_scale == 1.0
assert "CPUExecutionProvider" in onnxruntime.get_available_providers()
print("Piper pronto: " + version("piper-tts"), flush=True)
