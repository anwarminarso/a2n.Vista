// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Optional integration with the built-in ASP.NET Core OpenAPI pipeline (Microsoft.AspNetCore.OpenApi),
// spec openapi-emitter task 7.2 / Requirement 11.4 / Decision Log D128.
//
// This whole file is TFM-guarded to net9.0+ because Microsoft.AspNetCore.OpenApi does not exist for
// net8.0. On net8.0 the package ships only the Vista-owned serve endpoint (MapVistaOpenApi, task 7.1);
// on net9.0/net10.0 it additionally ships this document transformer, which merges the Vista-authored
// paths/components into an app's built-in pipeline document so a host already using AddOpenApi() sees the
// Vista views in its /openapi/{document}.json output.
//
// The one wrinkle is that the two supported TFMs pull incompatible Microsoft.OpenApi majors transitively:
//   - net9.0  -> Microsoft.AspNetCore.OpenApi 9.0.x  -> Microsoft.OpenApi 1.6.x  (types in
//     Microsoft.OpenApi.Models; OpenApiSchema.Type is a string; enum values are IOpenApiAny; references
//     are an OpenApiReference on the concrete type; nullability is a bool).
//   - net10.0 -> Microsoft.AspNetCore.OpenApi 10.0.x -> Microsoft.OpenApi 2.0.x  (types in
//     Microsoft.OpenApi; OpenApiSchema.Type is a JsonSchemaType flags enum; enum values are JsonNode;
//     references are dedicated OpenApiSchemaReference/OpenApiSecuritySchemeReference holders; nullability
//     is the JsonSchemaType.Null flag; schema collections are typed against IOpenApiSchema).
// The mapping therefore has two version-specific branches (see the #if NET10_0_OR_GREATER split in the
// mapper). Everything else — the transformer shell, the Vista document build, the merge orchestration and
// its "add-if-absent / skip-if-exists" semantics — is shared.

#if NET9_0_OR_GREATER

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using VM = a2n.Vista.OpenApi.Model;
#if NET10_0_OR_GREATER
using System.Text.Json.Nodes;
using OA = Microsoft.OpenApi;
#else
using Microsoft.OpenApi.Any;
using OA = Microsoft.OpenApi.Models;
#endif

namespace a2n.Vista.OpenApi.AspNetCorePipeline;

/// <summary>
/// An <see cref="IOpenApiDocumentTransformer"/> that merges the Vista-authored OpenAPI document (built by
/// <see cref="VistaOpenApiDocumentBuilder"/> from the live <c>IViewRegistry</c> and the serialization seam)
/// into the built-in ASP.NET Core OpenAPI pipeline's document (spec openapi-emitter, task 7.2;
/// Requirement 11.4; Decision Log D128). Register it via
/// <c>AddOpenApi(o =&gt; o.AddVistaOpenApiTransformer())</c> or the turnkey
/// <c>services.AddVistaOpenApiPipelineIntegration()</c>; both require <c>AddVistaOpenApi(...)</c> to have
/// registered the builder in DI.
/// </summary>
/// <remarks>
/// <para>
/// <b>Merge semantics (non-destructive, never throws on collision).</b> Vista <c>paths</c> are added
/// <em>only when the target document does not already declare that path</em> (skip-if-exists), so an app's
/// own endpoints always win over a Vista path on the same route. Vista <c>components.schemas</c> and
/// <c>components.securitySchemes</c> are added <em>only when the target has no entry under that name</em>
/// (add-if-absent). Schemas are merged before paths so the operation <c>$ref</c>s resolve against
/// components that are already present.
/// </para>
/// <para>
/// <b>AOT posture (Requirement 13.3).</b> Building the Vista document dips into the RUC per-view DTO schema
/// generation, so the reflection is genuine here. The registration entry points
/// (<c>AddVistaOpenApiTransformer</c> / <c>AddVistaOpenApiPipelineIntegration</c>) carry
/// <see cref="RequiresUnreferencedCodeAttribute"/>; <see cref="TransformAsync"/> cannot itself be RUC-marked
/// because it implements a non-RUC interface member, so the single build call is suppressed with a
/// justification instead.
/// </para>
/// </remarks>
public sealed class VistaOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly VistaOpenApiDocumentBuilder _builder;
    private readonly object _gate = new();
    private VM.OpenApiDocument? _vistaDocument;

    /// <summary>Creates the transformer over the DI-resolved Vista document <paramref name="builder"/>.</summary>
    /// <param name="builder">The metadata-driven Vista OpenAPI document builder (registered by <c>AddVistaOpenApi</c>).</param>
    public VistaOpenApiDocumentTransformer(VistaOpenApiDocumentBuilder builder)
    {
        System.ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <summary>
    /// Merges the Vista document's paths and components into <paramref name="document"/>. The Vista document
    /// is built once per transformer instance and cached, then merged with add-if-absent / skip-if-exists
    /// semantics.
    /// </summary>
    /// <param name="document">The pipeline document to augment (mutated in place).</param>
    /// <param name="context">The transformer context (unused; the builder reads the live registry via DI).</param>
    /// <param name="cancellationToken">A cancellation token (the merge is synchronous and does not block).</param>
    /// <returns>A completed task.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "The Vista document build reflects over per-view DTO types by design; the registration "
            + "entry points (AddVistaOpenApiTransformer / AddVistaOpenApiPipelineIntegration) are RUC-annotated "
            + "so callers opt into this honestly. TransformAsync cannot be RUC-marked as it implements a "
            + "non-RUC interface member.")]
    public Task TransformAsync(
        OA.OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(document);

        var vista = _vistaDocument;
        if (vista is null)
        {
            lock (_gate)
            {
                vista = _vistaDocument ??= _builder.Build();
            }
        }

        Merge(vista, document);
        return Task.CompletedTask;
    }

    // ---- Merge orchestration (shared across versions) --------------------------------------------

    private static void Merge(VM.OpenApiDocument vista, OA.OpenApiDocument target)
    {
        target.Paths ??= new OA.OpenApiPaths();
        target.Components ??= new OA.OpenApiComponents();

        // components.schemas — add-if-absent (map before paths so operation $refs resolve).
        if (vista.Components?.Schemas is { Count: > 0 } schemas)
        {
#if NET10_0_OR_GREATER
            target.Components.Schemas ??= new Dictionary<string, OA.IOpenApiSchema>();
#else
            target.Components.Schemas ??= new Dictionary<string, OA.OpenApiSchema>();
#endif
            foreach (var pair in schemas)
            {
                if (!target.Components.Schemas.ContainsKey(pair.Key))
                {
                    target.Components.Schemas[pair.Key] = MapSchema(pair.Value, target);
                }
            }
        }

        // components.securitySchemes — add-if-absent.
        if (vista.Components?.SecuritySchemes is { Count: > 0 } securitySchemes)
        {
#if NET10_0_OR_GREATER
            target.Components.SecuritySchemes ??= new Dictionary<string, OA.IOpenApiSecurityScheme>();
#else
            target.Components.SecuritySchemes ??= new Dictionary<string, OA.OpenApiSecurityScheme>();
#endif
            foreach (var pair in securitySchemes)
            {
                if (!target.Components.SecuritySchemes.ContainsKey(pair.Key))
                {
                    target.Components.SecuritySchemes[pair.Key] = MapSecurityScheme(pair.Value);
                }
            }
        }

        // paths — skip-if-exists (an app's own endpoint on the same route always wins).
        if (vista.Paths is { Count: > 0 } paths)
        {
            foreach (var pair in paths)
            {
                if (!target.Paths.ContainsKey(pair.Key))
                {
                    target.Paths[pair.Key] = MapPathItem(pair.Value, target);
                }
            }
        }
    }

    // ---- Path / operation mapping ----------------------------------------------------------------

    private static OA.OpenApiPathItem MapPathItem(VM.OpenApiPathItem item, OA.OpenApiDocument host)
    {
#if NET10_0_OR_GREATER
        var operations = new Dictionary<System.Net.Http.HttpMethod, OA.OpenApiOperation>();
        if (item.Get is not null)
        {
            operations[System.Net.Http.HttpMethod.Get] = MapOperation(item.Get, host);
        }

        if (item.Post is not null)
        {
            operations[System.Net.Http.HttpMethod.Post] = MapOperation(item.Post, host);
        }
#else
        var operations = new Dictionary<OA.OperationType, OA.OpenApiOperation>();
        if (item.Get is not null)
        {
            operations[OA.OperationType.Get] = MapOperation(item.Get, host);
        }

        if (item.Post is not null)
        {
            operations[OA.OperationType.Post] = MapOperation(item.Post, host);
        }
#endif
        return new OA.OpenApiPathItem { Operations = operations };
    }

    private static OA.OpenApiOperation MapOperation(VM.OpenApiOperation operation, OA.OpenApiDocument host)
    {
        var result = new OA.OpenApiOperation
        {
            OperationId = operation.OperationId,
            Summary = operation.Summary,
            Description = operation.Description,
            Responses = MapResponses(operation.Responses, host),
        };

        if (operation.Parameters is { Count: > 0 })
        {
#if NET10_0_OR_GREATER
            result.Parameters = operation.Parameters
                .Select(p => (OA.IOpenApiParameter)MapParameter(p, host)).ToList();
#else
            result.Parameters = operation.Parameters.Select(p => MapParameter(p, host)).ToList();
#endif
        }

        if (operation.RequestBody is not null)
        {
            result.RequestBody = MapRequestBody(operation.RequestBody, host);
        }

        if (operation.Security is { Count: > 0 })
        {
            result.Security = MapSecurity(operation.Security, host);
        }

        return result;
    }

    private static OA.OpenApiResponses MapResponses(
        IReadOnlyDictionary<string, VM.OpenApiResponse>? responses,
        OA.OpenApiDocument host)
    {
        var result = new OA.OpenApiResponses();
        if (responses is not null)
        {
            foreach (var pair in responses)
            {
                result[pair.Key] = MapResponse(pair.Value, host);
            }
        }

        return result;
    }

    private static OA.OpenApiResponse MapResponse(VM.OpenApiResponse response, OA.OpenApiDocument host)
    {
        var result = new OA.OpenApiResponse { Description = response.Description ?? string.Empty };

        if (response.Headers is { Count: > 0 })
        {
#if NET10_0_OR_GREATER
            result.Headers = response.Headers
                .ToDictionary(p => p.Key, p => (OA.IOpenApiHeader)MapHeader(p.Value, host));
#else
            result.Headers = response.Headers.ToDictionary(p => p.Key, p => MapHeader(p.Value, host));
#endif
        }

        if (response.Content is { Count: > 0 })
        {
            result.Content = response.Content.ToDictionary(p => p.Key, p => MapMediaType(p.Value, host));
        }

        return result;
    }

    private static OA.OpenApiRequestBody MapRequestBody(VM.OpenApiRequestBody body, OA.OpenApiDocument host)
    {
        var result = new OA.OpenApiRequestBody
        {
            Description = body.Description,
            Required = body.Required ?? false,
        };

        if (body.Content is { Count: > 0 })
        {
            result.Content = body.Content.ToDictionary(p => p.Key, p => MapMediaType(p.Value, host));
        }

        return result;
    }

    private static OA.OpenApiMediaType MapMediaType(VM.OpenApiMediaType media, OA.OpenApiDocument host) => new()
    {
        Schema = media.Schema is null ? null : MapSchema(media.Schema, host),
    };

    private static OA.OpenApiParameter MapParameter(VM.OpenApiParameter parameter, OA.OpenApiDocument host) => new()
    {
        Name = parameter.Name,
        In = MapParameterLocation(parameter.In),
        Description = parameter.Description,
        Required = parameter.Required ?? false,
        Schema = parameter.Schema is null ? null : MapSchema(parameter.Schema, host),
    };

    private static OA.OpenApiHeader MapHeader(VM.OpenApiHeader header, OA.OpenApiDocument host) => new()
    {
        Description = header.Description,
        Schema = header.Schema is null ? null : MapSchema(header.Schema, host),
    };

    // ---- Security mapping ------------------------------------------------------------------------

    private static IList<OA.OpenApiSecurityRequirement> MapSecurity(
        IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>> security,
        OA.OpenApiDocument host)
    {
        var result = new List<OA.OpenApiSecurityRequirement>();
        foreach (var requirement in security)
        {
            var mapped = new OA.OpenApiSecurityRequirement();
            foreach (var pair in requirement)
            {
#if NET10_0_OR_GREATER
                mapped[new OA.OpenApiSecuritySchemeReference(pair.Key, host)] = pair.Value.ToList();
#else
                var schemeReference = new OA.OpenApiSecurityScheme
                {
                    Reference = new OA.OpenApiReference
                    {
                        Type = OA.ReferenceType.SecurityScheme,
                        Id = pair.Key,
                    },
                };
                mapped[schemeReference] = pair.Value.ToList();
#endif
            }

            result.Add(mapped);
        }

        return result;
    }

    private static OA.OpenApiSecurityScheme MapSecurityScheme(VM.OpenApiSecurityScheme scheme) => new()
    {
        Type = MapSecuritySchemeType(scheme.Type),
        Scheme = scheme.Scheme,
        BearerFormat = scheme.BearerFormat,
        Description = scheme.Description,
        Name = scheme.Name,
#if NET10_0_OR_GREATER
        In = scheme.In is null ? null : MapParameterLocation(scheme.In),
#else
        In = MapParameterLocation(scheme.In) ?? OA.ParameterLocation.Header,
#endif
    };

    // ---- Schema mapping (the version-divergent core) ---------------------------------------------

#if NET10_0_OR_GREATER
    private static OA.IOpenApiSchema MapSchema(VM.OpenApiSchema schema, OA.OpenApiDocument host)
    {
        // A pure reference: emit a dedicated schema-reference holder (2.x drops the on-type reference).
        if (schema.Ref is not null)
        {
            return new OA.OpenApiSchemaReference(ReferenceId(schema.Ref), host);
        }

        var result = new OA.OpenApiSchema
        {
            Format = schema.Format,
            Description = schema.Description,
        };

        // Type + nullability (3.1 models null as a union member of the JsonSchemaType flags).
        var type = MapJsonSchemaType(schema.Type);
        if (type is not null)
        {
            result.Type = schema.Nullable == true ? type.Value | OA.JsonSchemaType.Null : type.Value;
        }
        else if (schema.Nullable == true)
        {
            result.Type = OA.JsonSchemaType.Null;
        }

        if (schema.Enum is { Count: > 0 })
        {
            result.Enum = schema.Enum.Select(value => (JsonNode)JsonValue.Create(value)!).ToList();
        }

        if (schema.Items is not null)
        {
            result.Items = MapSchema(schema.Items, host);
        }

        if (schema.Properties is { Count: > 0 })
        {
            result.Properties = schema.Properties.ToDictionary(p => p.Key, p => MapSchema(p.Value, host));
        }

        if (schema.OneOf is { Count: > 0 })
        {
            result.OneOf = schema.OneOf.Select(s => MapSchema(s, host)).ToList();
        }

        if (schema.Required is { Count: > 0 })
        {
            result.Required = new HashSet<string>(schema.Required);
        }

        // Open-map semantics must survive the 2.x mapping exactly as they do on the 1.x branch below:
        // without this, the merged ProblemDetails schema and every dictionary-shaped schema silently lose
        // `additionalProperties` on net10, so the same app emits a different document per target framework.
        if (schema.AdditionalProperties is not null)
        {
            result.AdditionalPropertiesAllowed = schema.AdditionalProperties.Value;
        }

        if (schema.Discriminator is not null)
        {
            result.Discriminator = MapDiscriminator(schema.Discriminator, host);
        }

        return result;
    }

    private static OA.OpenApiDiscriminator MapDiscriminator(VM.OpenApiDiscriminator discriminator, OA.OpenApiDocument host)
    {
        var result = new OA.OpenApiDiscriminator { PropertyName = discriminator.PropertyName };
        if (discriminator.Mapping is { Count: > 0 })
        {
            result.Mapping = discriminator.Mapping
                .ToDictionary(p => p.Key, p => new OA.OpenApiSchemaReference(ReferenceId(p.Value), host));
        }

        return result;
    }

    private static OA.JsonSchemaType? MapJsonSchemaType(string? type) => type switch
    {
        "string" => OA.JsonSchemaType.String,
        "integer" => OA.JsonSchemaType.Integer,
        "number" => OA.JsonSchemaType.Number,
        "boolean" => OA.JsonSchemaType.Boolean,
        "object" => OA.JsonSchemaType.Object,
        "array" => OA.JsonSchemaType.Array,
        "null" => OA.JsonSchemaType.Null,
        _ => null,
    };
#else
    private static OA.OpenApiSchema MapSchema(VM.OpenApiSchema schema, OA.OpenApiDocument host)
    {
        // A pure reference: 1.x carries the reference on the concrete schema type.
        if (schema.Ref is not null)
        {
            return new OA.OpenApiSchema
            {
                Reference = new OA.OpenApiReference
                {
                    Type = OA.ReferenceType.Schema,
                    Id = ReferenceId(schema.Ref),
                },
            };
        }

        var result = new OA.OpenApiSchema
        {
            Type = schema.Type,
            Format = schema.Format,
            Nullable = schema.Nullable ?? false,
            Description = schema.Description,
        };

        if (schema.Enum is { Count: > 0 })
        {
            result.Enum = schema.Enum.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }

        if (schema.Items is not null)
        {
            result.Items = MapSchema(schema.Items, host);
        }

        if (schema.Properties is { Count: > 0 })
        {
            result.Properties = schema.Properties.ToDictionary(p => p.Key, p => MapSchema(p.Value, host));
        }

        if (schema.OneOf is { Count: > 0 })
        {
            result.OneOf = schema.OneOf.Select(s => MapSchema(s, host)).ToList();
        }

        if (schema.Required is { Count: > 0 })
        {
            result.Required = new HashSet<string>(schema.Required);
        }

        if (schema.AdditionalProperties is not null)
        {
            result.AdditionalPropertiesAllowed = schema.AdditionalProperties.Value;
        }

        if (schema.Discriminator is not null)
        {
            result.Discriminator = MapDiscriminator(schema.Discriminator);
        }

        return result;
    }

    private static OA.OpenApiDiscriminator MapDiscriminator(VM.OpenApiDiscriminator discriminator)
    {
        var result = new OA.OpenApiDiscriminator { PropertyName = discriminator.PropertyName };
        if (discriminator.Mapping is { Count: > 0 })
        {
            result.Mapping = discriminator.Mapping.ToDictionary(p => p.Key, p => p.Value);
        }

        return result;
    }
#endif

    // ---- Shared enum/string helpers --------------------------------------------------------------

    /// <summary>Extracts the component id from a local <c>#/components/schemas/{id}</c> reference string.</summary>
    private static string ReferenceId(string reference)
    {
        var slash = reference.LastIndexOf('/');
        return slash >= 0 ? reference[(slash + 1)..] : reference;
    }

    private static OA.ParameterLocation? MapParameterLocation(string? location) => location switch
    {
        "query" => OA.ParameterLocation.Query,
        "header" => OA.ParameterLocation.Header,
        "path" => OA.ParameterLocation.Path,
        "cookie" => OA.ParameterLocation.Cookie,
        null => null,
        _ => OA.ParameterLocation.Header,
    };

    private static OA.SecuritySchemeType MapSecuritySchemeType(string type) => type.ToLowerInvariant() switch
    {
        "apikey" => OA.SecuritySchemeType.ApiKey,
        "http" => OA.SecuritySchemeType.Http,
        "oauth2" => OA.SecuritySchemeType.OAuth2,
        "openidconnect" => OA.SecuritySchemeType.OpenIdConnect,
        _ => OA.SecuritySchemeType.Http,
    };
}

#endif
