# CLAUDE.md — Native.IdentityCore (support-library)

This repository is the **shared-kernel support library** for Swepay's identity products
(Native Biometrics, Native KYC, and future 3DS work). It wraps Amazon Rekognition — face
liveness, per-tenant face indexing, a pure decision policy, and a KMS-backed assertion signer —
so no downstream backend re-implements biometric decisioning, collection naming, or JWS signing
against Rekognition/KMS by hand.

## What lives here

- `src/Native.IdentityCore/Liveness/` — `ILivenessSessionService` + `RekognitionLivenessSessionService`
  (`CreateFaceLivenessSession` / `GetFaceLivenessSessionResults`).
- `src/Native.IdentityCore/FaceIndex/` — `IFaceIndex` + `RekognitionFaceIndex` (`IndexFaces` /
  `SearchFacesByImage` / `DeleteFaces` / `ListFaces`), and `FaceCollectionNaming`
  (`swepay-{product}-{environment}-{tenantId}`) — the single place a Rekognition collection id
  is constructed or parsed.
- `src/Native.IdentityCore/Policy/` — `BiometricPolicy` (thresholds) + `BiometricDecision` (pure
  evaluator, zero AWS dependency, the most heavily unit-tested module in this repo).
- `src/Native.IdentityCore/Assertions/` — `BiometricAssertion` + `IAssertionSigner` +
  `KmsAssertionSigner` (ES256/RS256 via KMS `Sign`) + `EcdsaSignatureConverter` (DER→JOSE, pure).
- `src/Native.IdentityCore/Serialization/` — the one `JsonSerializerContext` for this library.

## Non-negotiables

- **API pública mínima, simétrica, versionada.** Breaking change to a public type/member →
  major version + `CHANGELOG.md` "Breaking Changes" section. This library has no
  `PublicAPI.Shipped.txt` yet (v0.1.0, still pre-1.0) — add one at 1.0.0.
- **AOT (GS-02):** `IsAotCompatible=true` + `EnableTrimAnalyzer=true` + `EnableAotAnalyzer=true`
  on the library csproj, `TreatWarningsAsErrors=true` at the repo root. No reflection, no
  `dynamic`, no `Newtonsoft.Json`. Every JSON call goes through
  `IdentityCoreJsonSerializerContext` — never `JsonSerializer.Serialize(obj)` without a
  `JsonTypeInfo<T>`.
- **No PII, no images, ever logged or persisted.** This library never writes to disk/S3/DynamoDB
  itself; it passes bytes through in-memory. Log statements carry `tenantId`/`sessionId`/counts,
  never confidence scores, similarity scores, or image bytes (GS-09).
- **Tenant isolation (GS-05):** every `IFaceIndex`/`ILivenessSessionService` method takes
  `tenantId` explicitly; collection ids are only ever built via `FaceCollectionNaming` — never
  concatenate `"swepay-" + tenantId` ad hoc anywhere else in this codebase.
- **`BiometricDecision.Evaluate` is pure.** No I/O, no `DateTime.Now`/`UtcNow`, no AWS SDK type in
  its signature. If you need Rekognition-shaped data in a policy decision, translate it to
  primitives (`double?`, `bool`) at the call site first.

## Testing (GS-07)

xUnit + NSubstitute + Shouldly + Bogus, `coverlet.runsettings` gate at 85% line / 70% branch.
`IAmazonRekognition`/`IAmazonKeyManagementService` are mocked with NSubstitute — this library
never talks to real AWS in a unit test. Watch out for the AWS SDK's own compile-time model
validators (`Rekognition1000`/`Rekognition1002` analyzers): `SessionId`/`FaceId` string literals
in test fixtures must look like UUIDs, or the build fails before tests even run.

## Agents (archetype support-library)

`architect`, `developer`, `qa`, `docs` — see `.claude/agents/`. No specialized agents yet; add
`aot-compliance-guard` if trim/AOT churn grows enough to warrant a dedicated reviewer.
