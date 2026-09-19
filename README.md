# Native.IdentityCore

Shared-kernel support library for Swepay identity products (Native Biometrics, Native KYC,
future 3DS): face liveness sessions, a per-tenant face index, a pure biometric decision policy,
and a KMS-backed signer for biometric assertions — all over Amazon Rekognition and AWS KMS.
Native AOT compatible.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## TL;DR

- **`ILivenessSessionService`** wraps Rekognition's Face Liveness API: start a session, poll its
  result (confidence, reference/audit images).
- **`IFaceIndex`** wraps a Rekognition face collection *per tenant*: index, search, delete — the
  collection id (`swepay-{product}-{env}-{tenantId}`) is always built the same way, never by hand.
- **`BiometricDecision.Evaluate`** turns raw scores into `Verified`/`Retry`/`Rejected` against a
  `BiometricPolicy` — pure function, no AWS SDK, fully unit-testable on its own.
- **`KmsAssertionSigner`** signs a `BiometricAssertion` into a compact JWS (ES256/RS256) using a
  KMS asymmetric key, so a relying party can verify it offline. Every assertion carries standard
  JWT claims (`iss`, `aud`, `sub`, `iat`, `exp`, `jti`) alongside the domain's own custom claims
  (`tenant_id`, `user_ref`, `purpose`, `decision`, scores).
- **`JwksAssertionVerifier`** is the counterpart: verifies a compact JWS against a published JWKS
  (`iss`/`aud`/`purpose`/`decision`/expiry), for a relying party that doesn't want to hand-roll it.
- This library never persists images, never writes to storage, and never logs a biometric score.

## Installation

```bash
dotnet add package Native.IdentityCore
```

## Quick start (3 steps)

### 1. Register the services you need

```csharp
using Native.IdentityCore;
using Native.IdentityCore.FaceIndex;
using Native.IdentityCore.Assertions;

services.AddSingleton<IAmazonRekognition>(new AmazonRekognitionClient());
services.AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient());

services.AddNativeIdentityCoreLiveness();
services.AddNativeIdentityCoreFaceIndex(new RekognitionFaceIndexOptions(Product: "kyc", Environment: "hml"));

// `Issuer` is required and validated as an absolute URI — read it from configuration
// (e.g. `ASSERTION_ISSUER`), never hardcode a domain. There is no confirmed public host for
// every identity product yet; let the consuming service's own config decide.
services.AddNativeIdentityCoreAssertionSigner(new KmsAssertionSignerOptions(
    "alias/identity-assertions", AssertionSigningAlgorithm.Es256, issuer: configuredIssuer));

// Only needed by a relying party that verifies assertions issued elsewhere (e.g. NativeGuard,
// Passly, or a third-party IdP) — most producers only need the signer above.
services.AddNativeIdentityCoreAssertionVerifier();
```

### 2. Run a liveness check, then enroll or verify a face

```csharp
// Start a session (hand sessionId to the client-side liveness capture SDK).
var session = await liveness.CreateSessionAsync(tenantId, new LivenessSessionOptions());

// ... client completes the capture flow ...

var result = await liveness.GetSessionResultAsync(tenantId, session.SessionId);
if (result.Status is LivenessSessionStatus.Succeeded && result.ReferenceImage is { } referenceImage)
{
    // Enrollment: index the captured face for this user.
    var indexed = await faceIndex.IndexAsync(tenantId, userRef, referenceImage.Value);

    // Verification: search the tenant collection for a match instead.
    var search = await faceIndex.SearchAsync(tenantId, referenceImage.Value);
}
```

### 3. Evaluate the decision and, if verified, issue a signed assertion

```csharp
var decision = BiometricDecision.Evaluate(
    policy: new BiometricPolicy(), // defaults: 90% liveness, 95% similarity, 3 attempts
    livenessConfidence: result.Confidence,
    faceSimilarity: search.BestMatch?.Similarity,
    faceMatchFound: search.Found,
    attemptNumber: 1);

if (decision.Outcome is BiometricOutcome.Verified)
{
    var assertion = new BiometricAssertion(
        tenantId, userRef, BiometricAssertionPurpose.Verification,
        result.Confidence, search.BestMatch?.Similarity, decision.Outcome,
        IssuedAt: timeProvider.GetUtcNow(), ExpiresAt: timeProvider.GetUtcNow().AddMinutes(5),
        Jti: correlationId);
        // Audience left at its default (null) -> the signer defaults `aud` to [tenantId];
        // pass Audience: [rpId] explicitly to target a different, tenant-configured audience.

    var jws = await assertionSigner.SignAsync(assertion);
    // hand `jws` to the relying party
}
```

### 4. (Relying party) Verify an assertion issued elsewhere

```csharp
var result = await assertionVerifier.VerifyAsync(
    new AssertionVerifierOptions(
        JwksUrl: "https://issuer-configured-per-tenant/.well-known/jwks.json",
        ExpectedIssuer: configuredIssuer,      // configuration — never a hardcoded domain
        ExpectedAudience: [tenantId],
        ExpectedPurpose: BiometricAssertionPurpose.Recovery,
        MaxAssertionAge: TimeSpan.FromMinutes(5),
        ClockSkew: TimeSpan.FromSeconds(30)),
    assertionJws,
    now: timeProvider.GetUtcNow());

if (result.IsValid)
{
    // result.Assertion!.Jti — track it yourself for replay protection; this library doesn't.
}
```

## What this library deliberately does NOT do

- **Does not persist images.** Bytes flow through in-memory; nothing is written to disk, S3, or a
  database by this library.
- **Does not decide anything by itself beyond `BiometricDecision`.** It hands you scores; you (the
  caller) apply your own `BiometricPolicy` and business rules on top if you need more than the
  default evaluator.
- **Does not expose an HTTP endpoint, queue consumer, or Lambda handler.** It is a plain library —
  wire it into your own `RoutedApiGatewayFunction`/handler.
- **Does not manage KMS keys or Rekognition collections' lifecycle beyond create/read/delete.**
  Key rotation, collection deletion policy, and IAM are the consuming service's responsibility.

## Anti-patterns

- Building a Rekognition collection id by hand (`"swepay-" + tenantId`) instead of
  `FaceCollectionNaming.Build(...)` — breaks tenant isolation guarantees and `TryParse` round-trips.
- Calling `IFaceIndex`/`ILivenessSessionService` without a real per-request `tenantId` — every
  method takes it explicitly on purpose (GS-05); there is no "current tenant" ambient context.
- Logging `LivenessSessionResult.Confidence`, `FaceSearchMatch.Similarity`, or any image bytes —
  this library's own logging never does, and neither should yours downstream.
- Treating a `Retry`/`Rejected` `BiometricAssertion` as authorization — only `Verified` assertions
  should let a relying party proceed; the others exist for audit trail.
- Hardcoding `KmsAssertionSignerOptions.Issuer`/`AssertionVerifierOptions.ExpectedIssuer` to a
  literal domain string — read it from your service's own configuration; a product may not have a
  confirmed public host yet, and the issuer can differ per environment (hml/prd).
- Trusting a caller-supplied `BiometricAssertion.Issuer` when signing — `KmsAssertionSigner` always
  stamps `iss` from its own `KmsAssertionSignerOptions.Issuer`, by design; that field on
  `BiometricAssertion` only carries a meaningful value on the verifier's *output*.
- Treating `IAssertionVerifier`'s successful result as replay-safe — it isn't. Track consumed
  `Jti` values yourself (e.g. a conditional-write replay guard) the same way NativeGuard/Passly do.

## Public API surface

`Native.IdentityCore.Liveness` — `ILivenessSessionService`, `RekognitionLivenessSessionService`,
`LivenessSessionOptions`, `LivenessSessionHandle`, `LivenessSessionResult`, `LivenessSessionStatus`.

`Native.IdentityCore.FaceIndex` — `IFaceIndex`, `RekognitionFaceIndex`, `RekognitionFaceIndexOptions`,
`FaceCollectionNaming`, `FaceIndexResult`, `FaceSearchResult`, `FaceSearchMatch`, `FaceIndexException`.

`Native.IdentityCore.Policy` — `BiometricPolicy`, `BiometricDecision`, `BiometricOutcome`,
`BiometricRejectionReason`, `BiometricDecisionResult`.

`Native.IdentityCore.Assertions` — `BiometricAssertion`, `BiometricAssertionPurpose`,
`IAssertionSigner`, `KmsAssertionSigner`, `KmsAssertionSignerOptions`, `AssertionSigningAlgorithm`,
`EcdsaSignatureConverter`, `AssertionPayload`, `IAssertionVerifier`, `JwksAssertionVerifier`,
`AssertionVerifierOptions`, `AssertionVerificationResult`, `AssertionVerificationError`,
`JwkDto`, `JwksDocumentDto`.

`Native.IdentityCore.Serialization` — `IdentityCoreJsonSerializerContext`, `AudienceJsonConverter`.

`Native.IdentityCore` — `ServiceCollectionExtensions` (`AddNativeIdentityCoreLiveness`,
`AddNativeIdentityCoreFaceIndex`, `AddNativeIdentityCoreAssertionSigner`,
`AddNativeIdentityCoreAssertionVerifier`).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security disclosure: [SECURITY.md](SECURITY.md).
