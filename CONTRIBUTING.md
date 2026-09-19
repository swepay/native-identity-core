# Contribuindo

Obrigado pelo interesse em contribuir com o `Native.IdentityCore`. Este documento resume o fluxo
de trabalho para propor mudanças neste repositório.

## Antes de Começar

- Busque na lista de issues se o problema já está mapeado.
- Para mudanças significativas (nova API pública, quebra de compatibilidade, nova dependência
  AWS), abra uma issue de discussão antes de codar.
- Este repositório é biblioteca de shared-kernel (`ECOSYSTEM_LIBRARIES.md`) — mudanças de
  contrato afetam todo consumidor (`native-biometrics-backend`, `native-kyc-backend`, futuros
  produtos de 3DS).

## Fluxo de Trabalho

1. Fork + branch a partir de `develop`.
2. Commit em mensagens claras no padrão [Conventional Commits](https://www.conventionalcommits.org/). Exemplos:
   - `feat(liveness): add S3 output config support`
   - `fix(assertions): correct DER-to-JOSE padding for P-384`
   - `chore(deps): bump AWSSDK.Rekognition`
3. Teste localmente — todo PR precisa passar o pipeline de CI.
4. Abra PR contra `develop` com descrição contendo: motivação, resumo técnico, checklist
   aplicável, exemplos quando relevante.
5. Revisão mínima de 1 mantenedor antes de merge.

## Padrões Técnicos

- **.NET:** código compila com `TreatWarningsAsErrors=true` e `Nullable=enable`.
  `IsAotCompatible=true` obrigatório — zero trim/AOT warnings novos (`IL2026`/`IL3050`/`IL2104`).
- **Serialização:** todo `System.Text.Json` passa por `IdentityCoreJsonSerializerContext` — nunca
  `JsonSerializer.Serialize(obj)` sem `JsonTypeInfo<T>`.
- **Testes:** xUnit + NSubstitute + Shouldly + Bogus (FluentAssertions e Jest são proibidos no
  ecossistema Swepay). Cobertura mínima 85% line / 70% branch (`coverlet.runsettings`).
- **Segredos/PII:** nenhuma imagem, score de confiança/similaridade ou payload de assertion vai
  para log. Reveja `Microsoft.Extensions.Logging` calls antes de abrir o PR.

## Comandos obrigatórios antes de abrir PR

```bash
dotnet build -c Release
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
dotnet format --verify-no-changes
```

## Quebra de Compatibilidade

Mudanças de API pública exigem:

- Descrição explícita no corpo do PR.
- Atualização de `CHANGELOG.md` sob seção "Breaking Changes".
- Bump de versão major assim que a lib estiver em `1.x` (hoje em `0.x`, ainda pré-1.0).

## Licença

Ao contribuir, você concorda que sua contribuição será licenciada sob os mesmos termos do
projeto (ver `LICENSE`).

## Contato

Dúvidas não-técnicas ou proposta de parceria: ops@swepay.com.br.
Vulnerabilidades: security@swepay.com.br (ver `SECURITY.md`).
