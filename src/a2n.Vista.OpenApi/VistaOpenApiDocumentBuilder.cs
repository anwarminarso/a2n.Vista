using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.Metadata;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Schema;
using a2n.Vista.OpenApi.Schemas;
using a2n.Vista.OpenApi.Serialization;
using a2n.Vista.Ports;
using a2n.Vista.Write;

namespace a2n.Vista.OpenApi;

/// <summary>
/// The metadata-driven OpenAPI document builder (Decision Log D127; spec openapi-emitter, task 5.x). It
/// maps each registered <see cref="ViewMetadata"/> to its exact <c>View_Operation_Set</c> and references
/// the fixed envelope, <c>FilterNode</c>, and per-view DTO schemas by <c>$ref</c>, so the emitted document
/// is, by construction, endpoint-parity-correct against the live route table (Requirement 1).
/// </summary>
/// <remarks>
/// <para>
/// The path/operation <b>structure</b> (paths, methods, operationIds, request/response <c>$ref</c>s) is
/// derived purely from <see cref="ViewMetadata"/> + the fixed facet table (<see cref="FacetOperations"/>)
/// and is reflection-free (Requirement 13.2). The one reflection branch is the per-view DTO schema
/// generation via the RUC <see cref="DtoSchemaGenerator"/>, which is why <see cref="Build"/> and
/// <see cref="BuildJson"/> are marked <see cref="RequiresUnreferencedCodeAttribute"/> (D96 asymmetry).
/// </para>
/// <para>
/// <b>Scope of tasks 5.2–5.3.</b> This builder emits the per-view operation skeleton: paths, methods,
/// operationIds, request bodies, and <b>success</b> responses, plus the collection of every referenced
/// component schema (envelopes, <c>FilterNode</c>, per-view <c>TRow</c>/<c>TCrud</c> DTOs). Task 5.3 layers
/// the per-facet <c>security</c> requirement (the configured or default HTTP bearer scheme when not
/// anonymous, none when anonymous), the RFC 7807 error responses (<c>400</c>/<c>403</c>/<c>404</c>/
/// <c>428</c>/<c>409</c>, all referencing the single <c>ProblemDetails</c> schema via
/// <c>application/problem+json</c>), the cached-metadata <c>If-None-Match</c>/<c>ETag</c>/<c>304</c>
/// contract, and the write-facet <c>ETag</c> success header for a token-bearing view. The final
/// deterministic ordering and full <c>info</c>/assembly version assembly are finalized by task 5.4.
/// Determinism is already honored here because every map is an ordinal-ordered dictionary
/// (<see cref="OpenApiCollections"/>).
/// </para>
/// </remarks>
public sealed class VistaOpenApiDocumentBuilder
{
    private readonly IViewRegistry _registry;
    private readonly JsonSerializerOptions _seamOptions;
    private readonly VistaEndpointOptions _endpointOptions;
    private readonly VistaOpenApiOptions _options;
    private readonly IWriteFacetRegistry? _writeFacets;

    /// <summary>
    /// Creates a builder over the registered views, the serialization seam's options (the schema/wire
    /// parity oracle), the AspNetCore endpoint options (the anonymity posture and metadata-caching toggle,
    /// consumed by task 5.3), and the emitter options.
    /// </summary>
    /// <param name="registry">The registry whose <see cref="IViewRegistry.All"/> views are documented.</param>
    /// <param name="seamOptions">The serialization seam's options (read, never modified).</param>
    /// <param name="endpointOptions">The AspNetCore endpoint options (read, never modified).</param>
    /// <param name="options">The emitter options (title, version, OpenAPI version, security, endpoint path).</param>
    /// <param name="writeFacets">
    /// The optional per-view Write-facet registry (read, never modified). It is the <b>only</b> place a
    /// view's optimistic-concurrency token is recorded (<see cref="Authoring.CrudFacetDefinition.ConcurrencyToken"/>);
    /// <see cref="Metadata.ViewMetadata"/> carries no token concept. When supplied, the emitter documents
    /// the token-gated <c>428</c>/<c>409</c> responses and the write-facet <c>ETag</c> success header for a
    /// writable view that declares a token (Requirements 3.5, 6.4). When <see langword="null"/>, no view is
    /// treated as token-bearing, so those token-conditional artifacts are omitted.
    /// </param>
    public VistaOpenApiDocumentBuilder(
        IViewRegistry registry,
        JsonSerializerOptions seamOptions,
        VistaEndpointOptions endpointOptions,
        VistaOpenApiOptions options,
        IWriteFacetRegistry? writeFacets = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(seamOptions);
        ArgumentNullException.ThrowIfNull(endpointOptions);
        ArgumentNullException.ThrowIfNull(options);

        _registry = registry;
        _seamOptions = seamOptions;
        _endpointOptions = endpointOptions;
        _options = options;
        _writeFacets = writeFacets;
    }

    /// <summary>
    /// The component names of the fixed Vista envelope schemas as they appear under
    /// <c>components.schemas</c>. Kept as constants so the request/response wiring and the schema
    /// registration agree on a single spelling.
    /// </summary>
    private static class EnvelopeNames
    {
        public const string ListRequest = "VistaListRequestBody";
        public const string SortBody = "VistaSortBody";
        public const string DetailRequest = "VistaDetailRequestBody";
        public const string WriteRequest = "VistaWriteRequestBody";
        public const string WriteResponse = "VistaWriteResponse";
        public const string MetadataResponse = "VistaMetadataResponse";
        public const string FieldMetadataResponse = "VistaFieldMetadataResponse";

        /// <summary>The single RFC 7807 problem-details schema every error response references (Requirement 6.1).</summary>
        public const string ProblemDetails = "ProblemDetails";
    }

    /// <summary>
    /// Builds the OpenAPI document for every registered view: the per-view operation set with request and
    /// success-response schemas, plus the collected component schemas.
    /// </summary>
    /// <returns>The assembled <see cref="OpenApiDocument"/>.</returns>
    [RequiresUnreferencedCode("Per-view DTO schema generation reflects over CLR row/write types.")]
    public OpenApiDocument Build()
    {
        var paths = OpenApiCollections.CreateMap<OpenApiPathItem>();
        var schemas = OpenApiCollections.CreateMap<OpenApiSchema>();
        var securitySchemes = OpenApiCollections.CreateMap<OpenApiSecurityScheme>();

        // --- Anonymity posture + the one-door security requirement (Requirement 7) -----------------
        // When not anonymous, the configured (or default HTTP bearer) scheme is emitted once under
        // components.securitySchemes and the *same* requirement is attached to every operation of every
        // view (R7.1/R7.4). When anonymous (AllowAnonymousAccess()), no scheme and no requirement is
        // emitted anywhere (R7.3).
        var anonymous = _endpointOptions.AllowAnonymous;
        IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? securityRequirement = null;
        if (!anonymous)
        {
            var scheme = _options.Security ?? DefaultBearerScheme;
            securitySchemes[scheme.Name] = new OpenApiSecurityScheme
            {
                Type = scheme.Type,
                Scheme = scheme.Scheme,
                BearerFormat = scheme.BearerFormat,
            };
            securityRequirement = BuildSecurityRequirement(scheme.Name);
        }

        var metadataCaching = _endpointOptions.EnableMetadataCaching;

        // A single generator across all views so shared nested DTO types are emitted once (Requirement 4.5).
        var generator = new DtoSchemaGenerator(_seamOptions);

        foreach (var view in _registry.All)
        {
            EmitView(view, generator, paths, schemas, securityRequirement, metadataCaching);
        }

        // Merge the per-view DTO component schemas (TRow/TCrud/nested) discovered by the RUC generator.
        foreach (var component in generator.Components)
        {
            if (!schemas.ContainsKey(component.Key))
            {
                schemas[component.Key] = component.Value;
            }
        }

        return new OpenApiDocument
        {
            Openapi = _options.OpenApiVersion,
            Info = new OpenApiInfo
            {
                // info.version derives from the host option, defaulting to the emitting assembly's
                // informational version (Requirement 8.4). See ResolveDocumentVersion.
                Title = _options.DocumentTitle,
                Version = ResolveDocumentVersion(_options.DocumentVersion),
            },
            Paths = paths.Count == 0 ? null : paths,
            Components = new OpenApiComponents
            {
                Schemas = schemas.Count == 0 ? null : schemas,
                SecuritySchemes = securitySchemes.Count == 0 ? null : securitySchemes,
            },
            // The document-level requirement mirrors the per-operation one; both are set when not anonymous
            // (harmless redundancy that keeps tooling that reads either level in agreement).
            Security = securityRequirement,
        };
    }

    /// <summary>
    /// The Vista default security scheme: an HTTP <c>bearer</c> scheme keyed <c>bearer</c> under
    /// <c>components.securitySchemes</c>, used when the host configures no scheme and the app is not
    /// anonymous (Requirement 7.1).
    /// </summary>
    private static readonly VistaSecurityScheme DefaultBearerScheme = new("bearer", "http", "bearer", null);

    /// <summary>
    /// Builds the one-door security requirement: a single alternative naming <paramref name="schemeName"/>
    /// with an empty scope list (HTTP bearer carries no scopes). Attached verbatim to every operation so
    /// the document reflects that one authorizer guards all facets (Requirement 7.4).
    /// </summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>> BuildSecurityRequirement(
        string schemeName)
    {
        var requirement = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [schemeName] = Array.Empty<string>(),
        };
        return new[] { (IReadOnlyDictionary<string, IReadOnlyList<string>>)requirement };
    }

    /// <summary>
    /// Serializes <see cref="Build"/> to its deterministic OpenAPI JSON representation via the source-gen
    /// serialization context.
    /// </summary>
    /// <returns>The document as compact, byte-stable JSON.</returns>
    [RequiresUnreferencedCode("Calls Build().")]
    public string BuildJson() => VistaOpenApiJson.Serialize(Build());

    /// <summary>
    /// The cleaned informational version of the emitting assembly (the assembly that owns this builder),
    /// computed once. Used as the <c>info.version</c> default when the host supplies no explicit version
    /// (Requirement 8.4).
    /// </summary>
    private static readonly string EmittingAssemblyVersion = ResolveEmittingAssemblyVersion();

    /// <summary>
    /// Resolves the document's <c>info.version</c>: the host-supplied <paramref name="configured"/> value
    /// when present, otherwise the emitting assembly's cleaned informational version (Requirement 8.4).
    /// </summary>
    /// <param name="configured">The host-supplied <see cref="VistaOpenApiOptions.DocumentVersion"/>, or <see langword="null"/>.</param>
    /// <returns>A non-empty version string suitable for <c>info.version</c>.</returns>
    internal static string ResolveDocumentVersion(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? EmittingAssemblyVersion : configured;

    /// <summary>
    /// Reads the emitting assembly's informational version (the assembly that owns
    /// <see cref="VistaOpenApiDocumentBuilder"/>), stripping any <c>+&lt;build-metadata&gt;</c> suffix a
    /// SourceLink build appends so the value is clean and stable. Falls back to the assembly's numeric
    /// <see cref="AssemblyName.Version"/> and finally to <c>1.0.0</c> when no version is discoverable.
    /// </summary>
    internal static string ResolveEmittingAssemblyVersion()
    {
        var assembly = typeof(VistaOpenApiDocumentBuilder).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return StripBuildMetadata(informational);
        }

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "1.0.0" : assemblyVersion;
    }

    /// <summary>
    /// Strips the SemVer build-metadata suffix (everything from the first <c>+</c>) from an informational
    /// version, so a SourceLink-appended <c>+&lt;git-sha&gt;</c> does not destabilize <c>info.version</c>.
    /// </summary>
    private static string StripBuildMetadata(string informationalVersion)
    {
        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informationalVersion[..plus] : informationalVersion;
    }

    [RequiresUnreferencedCode("Per-view DTO schema generation reflects over CLR row/write types.")]
    private void EmitView(
        ViewMetadata view,
        DtoSchemaGenerator generator,
        SortedDictionary<string, OpenApiPathItem> paths,
        SortedDictionary<string, OpenApiSchema> schemas,
        IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? securityRequirement,
        bool metadataCaching)
    {
        // A writable view is "token-bearing" iff it declares an optimistic-concurrency token on its Write
        // facet. That token lives *only* on the Write-facet registry (CrudFacetDefinition.ConcurrencyToken)
        // — ViewMetadata carries no token concept — so without the registry the view is treated as
        // tokenless and the 428/409 responses + write ETag header are omitted (Requirements 3.5, 6.4).
        var hasConcurrencyToken =
            !view.IsReadOnly
            && _writeFacets is not null
            && _writeFacets.TryGet(view.Name, out var facet)
            && facet.ConcurrencyToken is not null;

        // --- Row (TRow) schema + the per-view ViewListResult<TRow> wrapper -------------------------
        var rowSchema = generator.GenerateSchema(view.QueryType);
        var rowRef = rowSchema.Ref;
        if (rowRef is null)
        {
            // A non-$ref (inline scalar/array/permissive) row: register a synthetic component so the list
            // wrapper and the detail response can reference a stable name (referential integrity).
            var syntheticName = view.Name + "Row";
            if (!schemas.ContainsKey(syntheticName))
            {
                schemas[syntheticName] = rowSchema;
            }

            rowRef = DtoSchemaGenerator.ComponentRef(syntheticName);
            rowSchema = new OpenApiSchema { Ref = rowRef };
        }

        var rowComponent = ComponentNameFromRef(rowRef);
        var listResultName = "ViewListResult_" + rowComponent;
        if (!schemas.ContainsKey(listResultName))
        {
            schemas[listResultName] = EnvelopeSchemas.ViewListResult(rowRef);
        }

        var listResultRef = DtoSchemaGenerator.ComponentRef(listResultName);

        // --- Write body specialized to TCrud for writable views ------------------------------------
        // Prefer referencing the view's TCrud from the write body's `model` slot, so the generated TCrud
        // component is meaningfully referenced rather than emitted unused. Falls back to the shared
        // permissive VistaWriteRequestBody when the view declares no CrudType.
        var writeRequestRef = DtoSchemaGenerator.ComponentRef(EnvelopeNames.WriteRequest);
        if (!view.IsReadOnly && view.CrudType is not null)
        {
            var crudSchema = generator.GenerateSchema(view.CrudType);
            if (crudSchema.Ref is not null)
            {
                var crudComponent = ComponentNameFromRef(crudSchema.Ref);
                var writeBodyName = EnvelopeNames.WriteRequest + "_" + crudComponent;
                if (!schemas.ContainsKey(writeBodyName))
                {
                    schemas[writeBodyName] = SpecializeWriteBody(crudSchema.Ref);
                }

                writeRequestRef = DtoSchemaGenerator.ComponentRef(writeBodyName);
            }
        }

        var route = view.Route.TrimEnd('/');

        foreach (var facetOperation in FacetOperations.ForView(view.IsReadOnly))
        {
            var path = route + "/" + facetOperation.PathSuffix;
            var operation = BuildOperation(
                view,
                facetOperation,
                rowSchema,
                listResultRef,
                writeRequestRef,
                schemas,
                securityRequirement,
                hasConcurrencyToken,
                metadataCaching);

            var pathItem = paths.TryGetValue(path, out var existing) ? existing : new OpenApiPathItem();
            paths[path] = facetOperation.HttpMethod == "GET"
                ? pathItem with { Get = operation }
                : pathItem with { Post = operation };
        }
    }

    [RequiresUnreferencedCode("Registers envelope schemas referenced by the operation.")]
    private static OpenApiOperation BuildOperation(
        ViewMetadata view,
        FacetOperation facet,
        OpenApiSchema rowSchema,
        string listResultRef,
        string writeRequestRef,
        SortedDictionary<string, OpenApiSchema> schemas,
        IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>? securityRequirement,
        bool hasConcurrencyToken,
        bool metadataCaching)
    {
        var tags = new[] { view.Name };
        var isCachedMetadata = facet.Facet == Facet.Metadata && metadataCaching;

        var requestBody = BuildRequestBody(facet, writeRequestRef, schemas);

        var successResponse = BuildSuccessResponse(facet, rowSchema, listResultRef, schemas);

        // Success-response headers: an ETag on the cached metadata GET (Requirement 2.4) and on a
        // token-bearing writable view's update/delete (Requirement 3.5). Both carry the row's current
        // entity-tag so a caller can round-trip it as a subsequent If-None-Match / If-Match.
        var emitSuccessEtag =
            isCachedMetadata
            || (hasConcurrencyToken && facet.ConcurrencyErrorsWhenTokenDeclared);
        if (emitSuccessEtag)
        {
            successResponse = successResponse with { Headers = EtagHeader() };
        }

        var responses = OpenApiCollections.CreateMap<OpenApiResponse>();
        responses["200"] = successResponse;

        // A 304 Not Modified (no body) completes the conditional-GET contract for cached metadata.
        if (isCachedMetadata)
        {
            responses["304"] = new OpenApiResponse
            {
                Description = "The metadata is unchanged since the supplied If-None-Match entity-tag.",
            };
        }

        // Unconditional error codes from the facet table (400 on every body operation; 404 on
        // detail/create/update/delete). The table is the single source; the matrix is never re-hardcoded.
        foreach (var code in facet.AlwaysErrorCodes)
        {
            responses[code.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                ProblemResponse(ErrorDescription(code), schemas);
        }

        // 403 on every operation when the app is not anonymous (Requirement 6.3): the security requirement
        // being non-null is exactly the "not anonymous" condition.
        if (facet.ForbiddenWhenNotAnonymous && securityRequirement is not null)
        {
            responses["403"] = ProblemResponse(ErrorDescription(403), schemas);
        }

        // 428/409 on update/delete for a token-bearing writable view (Requirement 6.4).
        if (facet.ConcurrencyErrorsWhenTokenDeclared && hasConcurrencyToken)
        {
            responses["409"] = ProblemResponse(ErrorDescription(409), schemas);
            responses["428"] = ProblemResponse(ErrorDescription(428), schemas);
        }

        return new OpenApiOperation
        {
            OperationId = view.Name + "_" + facet.PathSuffix,
            Summary = facet.Facet + " " + view.Name,
            Tags = tags,
            // No path parameters on the action-style surface (Requirement 2.3). The cached metadata GET
            // documents the conditional-request If-None-Match header (Requirement 2.4).
            Parameters = isCachedMetadata ? new[] { IfNoneMatchParameter() } : null,
            RequestBody = requestBody,
            Responses = responses,
            // The same one-door requirement on every facet, or null when the app is anonymous (R7.3/R7.4).
            Security = securityRequirement,
        };
    }

    /// <summary>The single string-schema <c>ETag</c> response header used by cached metadata and token writes.</summary>
    private static IReadOnlyDictionary<string, OpenApiHeader> EtagHeader()
    {
        var headers = OpenApiCollections.CreateMap<OpenApiHeader>();
        headers["ETag"] = new OpenApiHeader
        {
            Description = "The current entity-tag of the resource, for optimistic concurrency / caching.",
            Schema = new OpenApiSchema { Type = "string" },
        };
        return headers;
    }

    /// <summary>The <c>If-None-Match</c> conditional-request header parameter for the cached metadata GET.</summary>
    private static OpenApiParameter IfNoneMatchParameter() => new()
    {
        Name = "If-None-Match",
        In = "header",
        Required = false,
        Description = "A previously returned ETag; when it still matches, the server responds 304 Not Modified.",
        Schema = new OpenApiSchema { Type = "string" },
    };

    /// <summary>
    /// Builds an <c>application/problem+json</c> error response referencing the single, once-registered
    /// <c>ProblemDetails</c> schema (Requirements 6.1, 6.5).
    /// </summary>
    private static OpenApiResponse ProblemResponse(string description, SortedDictionary<string, OpenApiSchema> schemas)
    {
        RegisterProblemDetails(schemas);
        var content = OpenApiCollections.CreateMap<OpenApiMediaType>();
        content["application/problem+json"] = new OpenApiMediaType
        {
            Schema = new OpenApiSchema { Ref = DtoSchemaGenerator.ComponentRef(EnvelopeNames.ProblemDetails) },
        };
        return new OpenApiResponse { Description = description, Content = content };
    }

    /// <summary>Registers the shared RFC 7807 <c>ProblemDetails</c> component schema once (Requirement 6.1).</summary>
    private static void RegisterProblemDetails(SortedDictionary<string, OpenApiSchema> schemas)
    {
        if (!schemas.ContainsKey(EnvelopeNames.ProblemDetails))
        {
            schemas[EnvelopeNames.ProblemDetails] = EnvelopeSchemas.ProblemDetails();
        }
    }

    /// <summary>A concise, standards-aligned description for each documented error status code.</summary>
    private static string ErrorDescription(int code) => code switch
    {
        400 => "The request envelope was invalid (validation or binding failure).",
        403 => "Access was denied by the view's authorizer.",
        404 => "The requested row was not found (or the operation is not available for this view).",
        409 => "The supplied concurrency token did not match the current row (concurrency conflict).",
        428 => "A concurrency token (If-Match) is required for this operation.",
        _ => "The request failed.",
    };

    [RequiresUnreferencedCode("Registers envelope schemas referenced by the request body.")]
    private static OpenApiRequestBody? BuildRequestBody(
        FacetOperation facet,
        string writeRequestRef,
        SortedDictionary<string, OpenApiSchema> schemas)
    {
        switch (facet.RequestBody)
        {
            case FacetRequestBody.None:
                return null;

            case FacetRequestBody.List:
                RegisterEnvelope(schemas, EnvelopeNames.ListRequest);
                return JsonRequestBody(
                    DtoSchemaGenerator.ComponentRef(EnvelopeNames.ListRequest),
                    "The neutral list/export query (filter, search, scope, sort, paging).");

            case FacetRequestBody.Detail:
                RegisterEnvelope(schemas, EnvelopeNames.DetailRequest);
                return JsonRequestBody(
                    DtoSchemaGenerator.ComponentRef(EnvelopeNames.DetailRequest),
                    "The row key: a scalar or a field-name to value object for a composite key.");

            case FacetRequestBody.Write:
                // writeRequestRef is either the shared VistaWriteRequestBody or a per-view specialization;
                // register the shared envelope only when the shared ref is in use.
                if (writeRequestRef == DtoSchemaGenerator.ComponentRef(EnvelopeNames.WriteRequest))
                {
                    RegisterEnvelope(schemas, EnvelopeNames.WriteRequest);
                }

                return JsonRequestBody(writeRequestRef, "The write payload (model) and optional key.");

            default:
                return null;
        }
    }

    [RequiresUnreferencedCode("Registers envelope schemas referenced by the success response.")]
    private static OpenApiResponse BuildSuccessResponse(
        FacetOperation facet,
        OpenApiSchema rowSchema,
        string listResultRef,
        SortedDictionary<string, OpenApiSchema> schemas)
    {
        switch (facet.SuccessBody)
        {
            case FacetSuccessBody.ViewListResult:
                return new OpenApiResponse
                {
                    Description = "The filtered, paged list result plus the unfiltered total.",
                    Content = JsonContent(new OpenApiSchema { Ref = listResultRef }),
                };

            case FacetSuccessBody.Row:
                return new OpenApiResponse
                {
                    Description = "The matching row.",
                    Content = JsonContent(rowSchema),
                };

            case FacetSuccessBody.Metadata:
                RegisterEnvelope(schemas, EnvelopeNames.MetadataResponse);
                return new OpenApiResponse
                {
                    Description = "The view's metadata descriptor.",
                    Content = JsonContent(new OpenApiSchema
                    {
                        Ref = DtoSchemaGenerator.ComponentRef(EnvelopeNames.MetadataResponse),
                    }),
                };

            case FacetSuccessBody.WriteResponse:
                RegisterEnvelope(schemas, EnvelopeNames.WriteResponse);
                return new OpenApiResponse
                {
                    Description = "The created row's primary key.",
                    Content = JsonContent(new OpenApiSchema
                    {
                        Ref = DtoSchemaGenerator.ComponentRef(EnvelopeNames.WriteResponse),
                    }),
                };

            case FacetSuccessBody.NoContentOr200:
                // A 200 with no body of interest; the ETag header / 204 variant is layered by task 5.3.
                return new OpenApiResponse { Description = "The operation succeeded." };

            default:
                return new OpenApiResponse { Description = "The operation succeeded." };
        }
    }

    /// <summary>
    /// Builds a per-view <c>VistaWriteRequestBody</c> whose <c>model</c> slot references the view's
    /// <c>TCrud</c> component, keeping the shared envelope shape for <c>key</c>.
    /// </summary>
    private static OpenApiSchema SpecializeWriteBody(string crudRef)
    {
        var baseline = EnvelopeSchemas.VistaWriteRequestBody();
        var properties = OpenApiCollections.CreateMap<OpenApiSchema>();
        if (baseline.Properties is not null)
        {
            foreach (var property in baseline.Properties)
            {
                properties[property.Key] = property.Value;
            }
        }

        properties["model"] = new OpenApiSchema { Ref = crudRef };
        return baseline with { Properties = properties };
    }

    private static OpenApiRequestBody JsonRequestBody(string schemaRef, string description) => new()
    {
        Required = true,
        Description = description,
        Content = JsonContent(new OpenApiSchema { Ref = schemaRef }),
    };

    private static IReadOnlyDictionary<string, OpenApiMediaType> JsonContent(OpenApiSchema schema)
    {
        var content = OpenApiCollections.CreateMap<OpenApiMediaType>();
        content["application/json"] = new OpenApiMediaType { Schema = schema };
        return content;
    }

    /// <summary>
    /// Registers a fixed Vista envelope schema (and its transitive dependencies) into
    /// <paramref name="schemas"/> once, by name. Because <paramref name="schemas"/> is an ordinal-ordered
    /// dictionary, registration order does not affect the serialized output (Requirement 9.2).
    /// </summary>
    private static void RegisterEnvelope(SortedDictionary<string, OpenApiSchema> schemas, string name)
    {
        if (schemas.ContainsKey(name))
        {
            return;
        }

        switch (name)
        {
            case EnvelopeNames.ListRequest:
                schemas[name] = EnvelopeSchemas.VistaListRequestBody();
                RegisterEnvelope(schemas, EnvelopeNames.SortBody);
                RegisterFilterNode(schemas);
                break;

            case EnvelopeNames.SortBody:
                schemas[name] = EnvelopeSchemas.VistaSortBody();
                break;

            case EnvelopeNames.DetailRequest:
                schemas[name] = EnvelopeSchemas.VistaDetailRequestBody();
                break;

            case EnvelopeNames.WriteRequest:
                schemas[name] = EnvelopeSchemas.VistaWriteRequestBody();
                break;

            case EnvelopeNames.WriteResponse:
                schemas[name] = EnvelopeSchemas.VistaWriteResponse();
                break;

            case EnvelopeNames.MetadataResponse:
                schemas[name] = EnvelopeSchemas.VistaMetadataResponse();
                RegisterEnvelope(schemas, EnvelopeNames.FieldMetadataResponse);
                break;

            case EnvelopeNames.FieldMetadataResponse:
                schemas[name] = EnvelopeSchemas.VistaFieldMetadataResponse();
                break;
        }
    }

    private static void RegisterFilterNode(SortedDictionary<string, OpenApiSchema> schemas)
    {
        foreach (var node in FilterNodeSchema.All())
        {
            if (!schemas.ContainsKey(node.Key))
            {
                schemas[node.Key] = node.Value;
            }
        }
    }

    private static string ComponentNameFromRef(string reference)
    {
        const string prefix = "#/components/schemas/";
        return reference.StartsWith(prefix, StringComparison.Ordinal)
            ? reference[prefix.Length..]
            : reference;
    }
}
