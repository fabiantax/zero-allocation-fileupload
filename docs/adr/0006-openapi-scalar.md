# 6. Built-in OpenAPI with Scalar

## Status

**Accepted** — 2026-09-22.

## Context

The ticket asks for a Swagger-style browser experience, but does not require the Swashbuckle
implementation. The service also explored Native AOT, for which reflection-heavy document
generation would add compatibility risk. The [decision log](../decision-log.md#22-swagger--scalar)
records the package choice. The AOT spike in [ADR 0001](0001-native-aot.md) published and ran the
selected stack, and PR #21 (`b5d7d8f`) put the same stack in the application before AOT itself was
parked.

## Decision

Generate the document with the .NET 10 built-in `Microsoft.AspNetCore.OpenApi` package and serve
the interactive browser client with `Scalar.AspNetCore`. Keep source-generated System.Text.Json
metadata for application JSON types. Expose the document at `/openapi/v1.json` and the UI at
`/scalar/v1`.

Do not add Swashbuckle. Scalar satisfies the user-facing requirement—a browser can inspect and
invoke the multipart endpoint—without making the product name “Swagger” the architecture. The
choice remains after AOT was parked because it is already proven, uses the platform document
generator, and no requirement justifies a second OpenAPI stack.

## Consequences

- The application has a working interactive API surface and an OpenAPI document using the same
  package combination proven in the native spike.
- Built-in document generation reduces reliance on reflection metadata and preserves an easier
  route back to Native AOT.
- This is a deliberate literal deviation from “Swagger UI”; reviewers or consumers expecting
  Swashbuckle-specific routes, filters, or extensions will not find them.
- Scalar is still a third-party dependency and serves a substantial JavaScript asset. Package
  upgrades must be checked against both the document and interactive upload/download flow.
- The chosen stack gives up the mature Swashbuckle extension ecosystem; a future customization
  requirement may require different tooling or explicit document transformers.
