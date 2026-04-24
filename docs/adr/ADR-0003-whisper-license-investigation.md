# ADR-0003: Whisper (onnx-community) license investigation

- Status: Draft
- Date: 2026-04-24

## Context

`src/BabelStudio.Inference/Runtime/ModelManifest/bundled-models.manifest.json`
has two entries for `onnx-community/whisper-tiny` (local bundle and
`whisper-tiny-onnx` variant). Both entries currently declare:

```json
"license": "unknown",
"commercial_allowed": false,
"redistribution_allowed": false,
"commercial_safe_mode": false
```

AGENTS.md rule 8 is explicit: "Do not invent commercial safety — unknown
license = unsafe." Before flipping these flags we need a confirmed license for
the exact artifact we ship, not a best-effort inference from an upstream repo.

## Decision

Keep the `onnx-community/whisper-tiny` manifest entries marked as
`license: "unknown"` with all commercial flags set to `false` until the license
can be confirmed for the specific Hugging Face revisions we bundle.

Blocking evidence gathered during this investigation:

- The upstream model card at
  [`openai/whisper-tiny`](https://huggingface.co/openai/whisper-tiny) is tagged
  `license:apache-2.0` in Hugging Face metadata, *not* MIT. The plan text
  hinted at "MIT" which is the license of the Whisper source code on GitHub
  ([`openai/whisper`](https://github.com/openai/whisper)), but the HF model
  weights for `openai/whisper-tiny` are declared Apache-2.0.
- The conversion repo
  [`onnx-community/whisper-tiny`](https://huggingface.co/onnx-community/whisper-tiny)
  does **not** declare any license tag in its Hugging Face metadata. Its model
  card describes itself only as "a conversion of
  https://huggingface.co/openai/whisper-tiny with ONNX weights to be compatible
  with Transformers.js" and does not state which license applies to the ONNX
  artifacts themselves.
- We therefore have two independent unknowns: (1) the plan's "MIT"
  assumption is not supported by the upstream HF metadata, and (2) the actual
  onnx-community repo has no declared license to copy.

Given rule 8 and the evidence above, updating the manifest to MIT /
`commercial_allowed: true` would "invent commercial safety" and is rejected.

## Follow-ups required before we can re-evaluate

1. Obtain an explicit license statement for the
   `onnx-community/whisper-tiny` revisions we bundle
   (`revision: "ea7c447ab3780e36818661384dea74a8e6b82fc6"` and the local-bundle
   revision). Options: file an issue / discussion on the HF repo asking the
   maintainers to add a `license:` field, or pin to a different Whisper ONNX
   source whose license is declared and compatible.
2. If the answer is Apache-2.0 (matching the upstream weights), re-check that
   Apache-2.0 is acceptable for `commercial_safe_mode` under Babel Studio's
   model policy and amend `MODEL_LICENSE_POLICY.md` if needed before flipping
   flags.
3. Once a confirmed license is in hand, update the two manifest entries in
   `src/BabelStudio.Inference/Runtime/ModelManifest/bundled-models.manifest.json`
   in a single change alongside a reference to that evidence (e.g. permalink
   to the HF revision that declares the license, or a copy of the license file
   stored under `models/whisper-tiny/`).

## Consequences

Positive:

- Babel Studio does not claim commercial safety it cannot prove.
- The decision is recorded so future contributors do not re-litigate or
  silently flip the flags without evidence.

Negative:

- The bundled Whisper-tiny entry remains blocked from commercial-safe runtime
  planning until the follow-ups above are completed. This is intentional under
  rule 8.

## Alternatives considered

### Treat the MIT license of the GitHub `openai/whisper` source code as the weight license

Rejected because source-code licenses do not automatically cover model weights
hosted as separate artifacts. The upstream HF model card explicitly tags
Apache-2.0 on the weights, which is different from the MIT license of the
training / inference code.

### Copy `openai/whisper-tiny`'s Apache-2.0 tag onto the onnx-community bundle

Rejected because the onnx-community repo is a separate artifact with no
declared license. Assuming the conversion inherits the upstream license is
exactly the "inventing commercial safety" pattern rule 8 forbids.

### Remove the Whisper entries from the manifest

Rejected for this ADR because the runtime still benchmarks against these
artifacts. Blocking them from commercial-safe mode via the existing flags is
sufficient until the license is confirmed.

## References

- AGENTS.md rule 8: "Do not invent commercial safety — unknown license =
  unsafe."
- `MODEL_LICENSE_POLICY.md`
- [Hugging Face: `onnx-community/whisper-tiny`](https://huggingface.co/onnx-community/whisper-tiny)
- [Hugging Face: `openai/whisper-tiny`](https://huggingface.co/openai/whisper-tiny)
  (tagged `license:apache-2.0`)
- [GitHub: `openai/whisper`](https://github.com/openai/whisper) (source code
  under MIT; separate artifact from the HF weights)
