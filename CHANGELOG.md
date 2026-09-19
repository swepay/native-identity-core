# Changelog

All notable changes to `Native.IdentityCore` are documented in this file. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[SemVer](https://semver.org/) (currently pre-1.0 — any release may include breaking changes,
called out explicitly below).

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
