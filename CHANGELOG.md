# Changelog

All notable changes to `Native.IdentityCore` are documented in this file. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[SemVer](https://semver.org/) (currently pre-1.0 — any release may include breaking changes,
called out explicitly below).

## [0.3.0] - 2026-09-19

Three gaps found while adopting 0.2.0 in `native-biometrics-backend` (PR #19) — see issues
#14/#15/#16.

### Added

- `Native.IdentityCore.Liveness`: `LivenessSessionResult` gains `ReferenceImageS3`/`AuditImagesS3`
  (`S3ImageReference`: bucket/key/optional version) alongside the existing inline `ReferenceImage`/
  `AuditImages`. Populated only when the session was created with
  `LivenessSessionOptions.OutputS3Bucket` configured — the two channels are mutually exclusive per
  Rekognition's own behavior, not both populated for the same session. **LGPD trade-off**
  (documented on `LivenessSessionOptions.OutputS3Bucket`): S3 output makes Rekognition durably
  persist the captured biometric image in the caller's bucket instead of the library handing back
  bytes in-memory and discarding them — the consuming service MUST configure an S3 Lifecycle
  expiration rule (or another erasure mechanism) consistent with its own retention/right-to-erasure
  policy for biometric data (GS-09); this library has no way to enforce or default one. Closes #14.
- `Native.IdentityCore.FaceIndex`: `IFaceIndex.DeleteByFaceIdAsync(tenantId, faceId)` — a direct
  `DeleteFaces` call with no `ListFaces` scan, for callers that already persisted
  `FaceIndexResult.FaceId` at enrollment time. `IFaceIndex.DeleteCollectionAsync(tenantId)` —
  deletes the tenant's entire face collection (e.g. LGPD/GS-12 tenant purge), idempotent against an
  already-absent collection. The existing `DeleteAsync(tenantId, userRef)` (list-then-delete by
  `ExternalImageId`) is unchanged. Closes #15.
- `Native.IdentityCore.Assertions`: `IAssertionKeyPublisher` + `KmsAssertionKeyPublisher` — the
  issuer-side counterpart to `JwksAssertionVerifier`. Builds a `JwksDocumentDto` from
  `kms:GetPublicKey` for one or more configured KMS key ids (`KmsAssertionKeyPublisherOptions.KeyIds`
  — current signing key first, then any previous key ids kept published during a rotation window),
  importing each response's DER `SubjectPublicKeyInfo` via `ECDsa.ImportSubjectPublicKeyInfo` (no
  manual ASN.1 parsing). Each published `JwkDto` has `kid` equal to its KMS key id (the same value
  `KmsAssertionSigner` stamps as the JWS `kid` header — no extra mapping needed), `kty: "EC"`,
  `crv: "P-256"`, `alg: "ES256"`, `use: "sig"`. Result is cached in memory (default 15 minutes,
  configurable via `KmsAssertionKeyPublisherOptions.CacheTtl` — matches `JwksAssertionVerifier`'s
  own JWKS cache TTL on the verify side). Only EC P-256 (`ECC_NIST_P256`) KMS keys are supported;
  any other `KeySpec` throws `NotSupportedException`. Registered via
  `AddNativeIdentityCoreAssertionKeyPublisher(...)`. Closes #16.
- `JwkDto` gains optional `Alg`/`Use` properties (RFC 7517 §4.4/§4.2) — populated by
  `KmsAssertionKeyPublisher`, ignored by `JwksAssertionVerifier` (which only reads
  `kty`/`kid`/`crv`/`x`/`y`), `null` (omitted from JSON) when not supplied.

### Breaking Changes

- `IFaceIndex` gains two new interface members (`DeleteByFaceIdAsync`, `DeleteCollectionAsync`) —
  source-breaking *only* for a custom `IFaceIndex` implementation outside this library; no such
  implementation exists across the four current consumers (`native-biometrics-backend`,
  `native-kyc-backend`, `native-guard-backend`, `native-passkey-backend`), all of which consume
  `RekognitionFaceIndex` as-is. Every existing call to `DeleteAsync(tenantId, userRef)` keeps
  compiling and behaving exactly as before.
- Note on issue #15's literal proposal: a same-named `DeleteAsync(tenantId, faceId)` overload is
  not possible in C# (both parameters are `string`, so it cannot be distinguished from the existing
  `DeleteAsync(tenantId, userRef)` overload by signature). `DeleteByFaceIdAsync` delivers the same
  intent (direct-by-id delete, no `ListFaces` scan) under a distinct name instead.

## [0.2.0] - 2026-09-19

### Added

- `Native.IdentityCore.Assertions`: `BiometricAssertion` gains `Issuer` (`iss`) and `Audience`
  (`aud`) so a relying party can bind an assertion to the issuing product/environment and to
  itself — the two gaps flagged as open questions by both `native-guard-backend`
  (`SPEC-guard-0001` §10) and `native-passkey-backend` (`SPEC-passkey-0002` §10).
  `AssertionPayload` adds the standard `iss`/`aud`/`sub` claims alongside every existing custom
  claim (`tenant_id`/`user_ref`/`purpose`/`decision`/scores/`jti`), which are unchanged.
- `KmsAssertionSignerOptions.Issuer` (required, validated as an absolute URI at
  `KmsAssertionSigner` construction) is the sole source of `iss` on signed assertions — a
  caller-supplied `BiometricAssertion.Issuer` is never used for signing, only for the verifier's
  parsed output (see below). Default audience strategy: an explicit, non-empty
  `BiometricAssertion.Audience` wins; otherwise it defaults to `[TenantId]`.
- `IAssertionVerifier` + `JwksAssertionVerifier`: the counterpart to `IAssertionSigner` this
  library's own spec (`SPEC-identity-core-0001` §10) deferred to "a future spec when there is a
  relying party concrete". Fetches/caches a JWKS (15 min TTL) over an injected `HttpClient`,
  verifies ES256 with manual base64url + `ECDsa.VerifyData` (no reflection, no external JWT
  library), and validates `iss`, `aud` (membership in an expected list), `exp`/`iat` (with clock
  skew and a max-age window), `purpose`, and `decision == Verified`. Replay protection (`jti`)
  remains the caller's responsibility — exposed on the returned `BiometricAssertion.Jti`.
  Registered via `AddNativeIdentityCoreAssertionVerifier()`.
- `AudienceJsonConverter`: (de)serializes the JWT `aud` claim as a single string when there is
  exactly one audience, or a JSON array otherwise (RFC 7519 §4.1.3) — manual, AOT-safe.

### Breaking Changes

- `BiometricAssertion.CorrelationId` renamed to `Jti` (still serialized as the `jti` claim — no
  wire format change). Existing named-argument call sites need `CorrelationId:` → `Jti:`.
- `KmsAssertionSignerOptions` gains a third required constructor parameter, `Issuer` (validated
  as an absolute URI — construction throws `ArgumentException` otherwise). Every call site must
  supply a real, configured issuer; never hardcode a domain.

## [0.1.0] - 2026-09-18

### Added

- `Native.IdentityCore.Liveness`: `ILivenessSessionService` + `RekognitionLivenessSessionService`
  over Amazon Rekognition's Face Liveness API (`CreateFaceLivenessSession` /
  `GetFaceLivenessSessionResults`).
- `Native.IdentityCore.FaceIndex`: `IFaceIndex` + `RekognitionFaceIndex` (index / search / delete
  faces per tenant) and `FaceCollectionNaming` (`swepay-{product}-{environment}-{tenantId}`).
- `Native.IdentityCore.Policy`: `BiometricPolicy` + `BiometricDecision`, a pure evaluator turning
  liveness/similarity scores into `Verified`/`Retry`/`Rejected`.
- `Native.IdentityCore.Assertions`: `BiometricAssertion`, `IAssertionSigner` +
  `KmsAssertionSigner` (ES256/RS256 compact JWS via AWS KMS `Sign`), `EcdsaSignatureConverter`
  (DER→JOSE signature conversion).
- `ServiceCollectionExtensions` for explicit DI registration
  (`AddNativeIdentityCoreLiveness`/`FaceIndex`/`AssertionSigner`).

### Breaking Changes

- N/A — initial release.
