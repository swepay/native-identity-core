# Changelog

All notable changes to `Native.IdentityCore` are documented in this file. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[SemVer](https://semver.org/) (currently pre-1.0 — any release may include breaking changes,
called out explicitly below).

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
