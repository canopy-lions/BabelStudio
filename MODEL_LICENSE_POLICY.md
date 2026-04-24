# Model License Policy

Babel Studio must track model licensing explicitly.

Every model should have a manifest entry with:

```json
{
  "model_id": "example/model",
  "task": "asr | translation | tts | diarization | vad | separation",
  "license": "MIT | Apache-2.0 | CC-BY-4.0 | CC-BY-NC-4.0 | custom | unknown",
  "commercial_allowed": true,
  "redistribution_allowed": true,
  "requires_attribution": false,
  "requires_user_consent": false,
  "voice_cloning": false,
  "commercial_safe_mode": true,
  "source_url": "",
  "revision": "",
  "sha256": ""
}
```

Rules:

- Non-commercial models must be disabled in commercial-safe mode.
- Unknown-license models must be disabled in commercial-safe mode.
- Voice-cloning models must require explicit consent flow.
- Attribution-required models must appear in export/project metadata where appropriate.
- Model licenses are independent from the app license.
