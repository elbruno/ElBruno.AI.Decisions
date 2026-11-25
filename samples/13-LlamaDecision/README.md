# Llama App decision sample

Start Llama App with a locally installed multimodal decision model. List installed
IDs at `http://localhost:9931/v1/models`, then run:

```powershell
dotnet run --project samples/13-LlamaDecision -- --model "LiquidAI/d1-omni-600M-GGUF:Q8_0"
dotnet run --project samples/13-LlamaDecision -- --image "C:\path\photo.png" --audio "C:\path\voice.wav"
dotnet run --project samples/13-LlamaDecision -- --offline
```

`--endpoint` overrides `http://localhost:9931/`. The supplied image and
recording are original sample media, described in [media/README.md](media/README.md).
User-supplied media is sent to the configured endpoint. Do not send sensitive
content to an untrusted server.

On November 25, 2026, the installed `d1-omni-600M` model returned plumbing for
the text example and red for the image example. It returned `other` for the
synthetic speech despite the recording mentioning a leaking tap. One example
does not establish model accuracy; evaluate audio and confidence thresholds on
representative data and keep a human review path.

In a follow-up check, the exact WAV bytes were verified in the `files` data URL
sent to the selected model. The server accepted both the original and separate
16 kHz synthetic plumbing and electrical recordings, but routed both speech
controls to `other`. A speech-detection question distinguished the controls
from a 440 Hz tone, while a question about leaking water did not identify the
plumbing control. This supports audio input reaching the model, not reliable
speech understanding. The upstream example uses a different, 2B model and
recording, so its reported result is not directly comparable.
