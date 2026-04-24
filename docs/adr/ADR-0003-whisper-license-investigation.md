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

## Re-verification (2026-04-24, post PR #3)

Re-checked Hugging Face's JSON API directly for both repos:

- `GET https://huggingface.co/api/models/openai/whisper-tiny` →
  `cardData.license == "apache-2.0"` (unchanged).
- `GET https://huggingface.co/api/models/onnx-community/whisper-tiny` →
  `cardData.license == null`; no `license:*` entry in `tags` either. The model
  card body still only says "a conversion of
  https://huggingface.co/openai/whisper-tiny with ONNX weights to be compatible
  with Transformers.js."

The situation is unchanged: the specific artifact we bundle has no declared
license. Rule 8 continues to require the manifest stay blocked.

`MODEL_LICENSE_POLICY.md` already lists `Apache-2.0` in its accepted license
enum and explicitly states "Unknown-license models must be disabled in
commercial-safe mode," so the policy side is already correct — the only
missing piece is *evidence for this specific artifact*.

## Follow-ups — pick one, in priority order

These are listed from most-preferred to least-preferred. Each is executable
and closes the ADR.

### Option 1 — Switch Whisper source to a repo with a declared license (preferred)

Replace `onnx-community/whisper-tiny` in `bundled-models.manifest.json` with
an ONNX Whisper-tiny artifact whose HF repo declares a license we accept
(Apache-2.0 or MIT). Candidates to evaluate:

- `microsoft/Phi-*` / Microsoft-published ONNX Whisper examples (usually
  MIT).
- Self-converted from `openai/whisper-tiny` using Optimum (see Option 3).

Work required: pick a repo, update both `onnx-community/whisper-tiny`
entries, re-run benchmarks to confirm parity, flip the commercial flags,
delete this ADR or mark it resolved.

Pro: simple, defensible provenance, no legal-ambiguity risk at ship time.
Con: extra benchmark run; the `whisper-tiny-local` alias / local bundle
needs to be re-downloaded.

### Option 2 — Get an explicit license declaration from onnx-community

File a discussion on
[`onnx-community/whisper-tiny`](https://huggingface.co/onnx-community/whisper-tiny/discussions)
asking the maintainers to add `license: apache-2.0` to the model card. Links
to include in the ask:

- The upstream `openai/whisper-tiny` repo declaring Apache-2.0.
- The HF model card metadata docs on `license:` fields.

If they accept and pin a revision, copy that revision hash into the manifest
and copy the `LICENSE` file they add into `models/whisper-tiny/` so the
evidence travels with the bundle.

Pro: keeps the existing bundle layout and benchmark corpus untouched.
Con: depends on external-maintainer response time; they may close without
action, in which case we fall back to Option 1 or 3.

### Option 3 — Self-convert `openai/whisper-tiny` and own the provenance

Run the conversion in-tree (or in a dedicated `tools/` script) using HF
Optimum / `onnxruntime-tools`, pin the exact upstream weight revision, and
publish the resulting ONNX files under `models/whisper-tiny/` with the
upstream Apache-2.0 license file copied in. Manifest entries then point at
the self-converted artifact with `license: Apache-2.0` and a `revision`
field documenting both the upstream weights revision and the conversion
script commit.

Pro: we control every bit of the artifact; provenance is tight.
Con: biggest upfront work; adds a conversion pipeline we then have to
maintain.

## Decision — choose Option 1 or 2 first; defer Option 3

Option 1 is the fastest unblock. Option 2 runs in parallel and is basically
free (one HF discussion). Whichever confirms a license first wins; the other
is cancelled. Option 3 is kept as an escape hatch if neither lands.

This ADR stays **Draft / in-effect** until one of the options completes.
Once confirmed, the follow-up PR should:

1. Update both `onnx-community/whisper-tiny` entries in
   `src/BabelStudio.Inference/Runtime/ModelManifest/bundled-models.manifest.json`
   (`license`, `commercial_allowed`, `redistribution_allowed`,
   `commercial_safe_mode`), with evidence linked in the PR.
2. Store the relevant license file under the corresponding
   `models/whisper-tiny*/` directory.
3. Flip this ADR's status to `Accepted` and summarize what evidence was
   obtained.

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
