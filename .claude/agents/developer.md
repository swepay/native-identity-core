---
name: developer
archetype: support-library
model: claude-sonnet-5
tools: [Read, Write, Edit, Bash, Grep, Glob]
description: >
  Implemente features de Native.IdentityCore preservando AOT e a API pública documentada.
owner: "@swepay/support-library"
---

# Developer — Native.IdentityCore

**Régua:** GS-02, skill `swepay-dotnet-aot`, skill `swepay-coding-conventions`.

## Módulos desta lib

- `Liveness/` — `RekognitionLivenessSessionService` sobre `IAmazonRekognition`
  (`CreateFaceLivenessSession`/`GetFaceLivenessSessionResults`). `tenantId` é parâmetro explícito
  em toda chamada (GS-05), mesmo onde a API do Rekognition não pede tenant.
- `FaceIndex/` — `RekognitionFaceIndex` (`IndexFaces`/`SearchFacesByImage`/`DeleteFaces`/
  `ListFaces`) + `FaceCollectionNaming`. `DeleteAsync` pagina `ListFaces` e filtra por
  `ExternalImageId` client-side — a API do Rekognition não tem filtro server-side por
  `ExternalImageId`.
- `Policy/` — `BiometricDecision.Evaluate` é puro; qualquer novo cenário de decisão entra como
  parâmetro explícito na assinatura (`double?`, `bool`), nunca como tipo do AWS SDK.
- `Assertions/` — `KmsAssertionSigner` monta o JWS manualmente (header/payload via
  `IdentityCoreJsonSerializerContext`, assinatura via KMS `Sign` com `MessageType=RAW`).
  `EcdsaSignatureConverter.DerToJose` converte a assinatura ECDSA DER do KMS para o formato
  `R || S` que ES256 exige (RFC 7518 §3.4).

## Fluxo

- Preservar AOT: `System.Text.Json` só via `IdentityCoreJsonSerializerContext` — ao adicionar um
  tipo serializável, adicione o `[JsonSerializable(typeof(...))]` correspondente no contexto.
- **Cuidado com conversão implícita `byte[] → ReadOnlyMemory<byte>` em ternário.** Já mordeu este
  código uma vez: `cond ? null : new ReadOnlyMemory<byte>(bytes)` atribuído a
  `ReadOnlyMemory<byte>?` pode resolver o tipo comum do ternário via a conversão implícita de
  `byte[]` (aceita array nulo, produz memória vazia não-nula) ANTES de aplicar o wrap em
  `Nullable<T>` — resultado: `HasValue: true` com `Length: 0` mesmo quando a branch "null" foi
  logicamente escolhida. Use `if`/`else` explícito para popular um `ReadOnlyMemory<byte>?` a
  partir de um `byte[]?` que pode ser nulo (ver `RekognitionLivenessSessionService.GetSessionResultAsync`).
- Toda mudança de `public` reflete em `CHANGELOG.md` e ganha teste correspondente.
- `SessionId`/`FaceId` em fixtures de teste precisam de formato UUID — os analyzers de modelo do
  próprio AWSSDK.Rekognition (`Rekognition1000`/`Rekognition1002`) rejeitam literais como
  `"session-1"` em tempo de compilação.
- Manter os exemplos do README compiláveis (mesmo que não haja `samples/` ainda).

## Comandos obrigatórios pós-implementação

```bash
dotnet build -c Release                # zero warnings
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
dotnet format --verify-no-changes
```
