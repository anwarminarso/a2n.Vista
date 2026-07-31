using a2n.Vista.Client.TypeScript.Acquire;
using a2n.Vista.Client.TypeScript.Cli;
using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Emit.Runtime;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Resolve;
using a2n.Vista.Client.TypeScript.Write;

namespace a2n.Vista.Client.TypeScript.Pipeline;

/// <summary>
/// The concrete buffered generation pipeline (task 12.2; design "The pipeline"): composes
/// <b>acquire → parse → resolve → model → emit → write</b> behind the <see cref="IPipelineRunner"/> seam the
/// CLI host (task 12.1) drives. Every stage that can fail routes through a single abort path — the first
/// <see cref="GenerationError"/> is returned on a failed <see cref="GenerationResult"/> and the run returns
/// immediately, having written nothing (Requirements 1.2, 1.4, 1.6, 1.8, 2.7). The whole
/// <see cref="GeneratedFile"/> set is buffered in memory and only committed to disk in the final write stage,
/// so a mid-generation failure can never leave a partial <c>Generated_Output</c> and any pre-existing output
/// is left untouched until the atomic write succeeds (Requirements 9.4, 10.7).
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is a faithful production counterpart of the determinism harness's output assembly: it emits
/// the six fixed, document-independent runtime files, <c>types.ts</c>, <c>filter-node.ts</c>, one
/// <c>views/{view}.ts</c> per mapped view, and — completing the surface — the <c>index.ts</c> barrel and the
/// English <c>README.md</c>. Non-fatal notices accumulate in a single shared <see cref="NoticeCollector"/>
/// across the model and emit stages and are handed back, deterministically ordered, on both the success and
/// failure results (Requirements 3.6, 3.7, 10.6).
/// </para>
/// <para>
/// <b>Security posture (Requirements 7.2, 7.5).</b> The operation graph is built with the document-level
/// <see cref="SecurityPosture"/> so that operations secured only by the document's top-level <c>security</c>
/// default — as the canonical Vista surface is (a single top-level bearer requirement, no per-operation
/// <c>security</c>) — are correctly classified as secured, and the generated view clients attach the
/// consumer-supplied credential accordingly.
/// </para>
/// </remarks>
public sealed class PipelineRunner : IPipelineRunner
{
    /// <inheritdoc />
    public async Task<GenerationResult> RunAsync(
        GenerationConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var notices = new NoticeCollector();

        // --- Acquire: read the raw document bytes from the configured file or HTTPS source (Req 1.1–1.4). ---
        using var source = CreateSource(config.Source);
        var acquired = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (acquired.IsError)
        {
            // A missing file or a failed/timed-out fetch aborts before any output (Requirements 1.2, 1.4).
            return GenerationResult.Failure(acquired.Error, notices.ToSortedList());
        }

        // --- Parse: bytes → internal OpenApiDocument, version-gated and well-formedness-checked (1.5, 1.6). --
        var parsed = OpenApiParser.Parse(acquired.Value);
        if (parsed.IsError)
        {
            return GenerationResult.Failure(parsed.Error, notices.ToSortedList());
        }

        // --- Resolve: confirm every local $ref targets a component; dangling → fatal (Requirements 1.7, 1.8). -
        var resolvedResult = RefResolver.Resolve(parsed.Value);
        if (resolvedResult.IsError)
        {
            return GenerationResult.Failure(resolvedResult.Error, notices.ToSortedList());
        }

        var resolved = resolvedResult.Value;

        // --- Model: bind the fixed envelopes; a missing required envelope aborts, naming it (Req 2.7). -------
        var envelopes = new EnvelopeCatalog().Bind(resolved, includeWriteEnvelopes: config.EmitWriteFacets);
        if (envelopes.IsError)
        {
            return GenerationResult.Failure(envelopes.Error, notices.ToSortedList());
        }

        // Re-lift the monomorphized ViewListResult_* components to the single generic pair (Requirement 2.6);
        // mismatches degrade to a non-fatal notice rather than aborting.
        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(resolved, notices);

        // The presence-discriminated FilterNode family; a missing union or variant aborts (Requirement 2.7).
        var filterModel = new FilterNodeModelBuilder().Build(resolved, notices);
        if (filterModel.IsError)
        {
            return GenerationResult.Failure(filterModel.Error, notices.ToSortedList());
        }

        // Classify the document-level security posture, then build the operation graph with it so operations
        // secured only by the document-level default are marked secured (Requirements 7.2, 7.5).
        var posture = new SecurityPostureBuilder().Build(resolved);
        var views = new OperationGraphBuilder().Build(resolved, reLift, notices, posture);

        // Every view name is derived from the untrusted document (an operationId or a path segment) and flows
        // into an emitted file path and TypeScript symbol. Validate the whole set here, before anything is
        // emitted, so an unsafe name is a typed abort rather than an unhandled exception in the emitter or a
        // write outside the output directory.
        var unsafeName = ViewNameGuard.FirstUnsafe(views);
        if (unsafeName is not null)
        {
            return GenerationResult.Failure(unsafeName, notices.ToSortedList());
        }

        // The per-view DTO component names to emit as interfaces: each view's by-name RowType/CrudType.
        var dtoComponentNames = CollectDtoComponentNames(views);

        // --- Emit: buffer every generated file in memory; nothing touches the filesystem yet (9.4, 10.7). ----
        var typesFile = TypesEmitter.Emit(
            new TypesEmitInput(resolved, envelopes.Value, reLift, dtoComponentNames, notices));
        if (typesFile.IsError)
        {
            // A per-view DTO or fallback component absent from components.schemas aborts (Requirement 2.7).
            return GenerationResult.Failure(typesFile.Error, notices.ToSortedList());
        }

        var viewNames = views.Select(view => view.ViewName).ToArray();

        var files = new List<GeneratedFile>
        {
            // The six fixed, document-independent runtime files.
            HttpTransportEmitter.Emit(),
            AuthEmitter.Emit(),
            ResultEmitter.Emit(),
            UrlEmitter.Emit(),
            ClientContextEmitter.Emit(),
            RawPayloadEmitter.Emit(),

            // The document-derived type surface.
            typesFile.Value,
            FilterNodeEmitter.Emit(filterModel.Value),

            // The barrel and the English usage documentation.
            IndexEmitter.Emit(viewNames),
            DocsEmitter.Emit(viewNames, config.EmitWriteFacets),
        };

        // One per-view client file, with the gated write facets included only when the run opts in.
        files.AddRange(ViewClientEmitter.EmitAll(views, config.EmitWriteFacets));

        // --- Write: commit the buffered set atomically; any failure leaves prior output untouched (9.4, 10.7). -
        var written = new OutputWriter().Write(files, config.OutputDirectory);
        if (written.IsError)
        {
            return GenerationResult.Failure(written.Error, notices.ToSortedList());
        }

        // Success: report the output directory, the count of generated views, and the ordered notices (10.6).
        return GenerationResult.Success(config.OutputDirectory, views.Count, notices.ToSortedList());
    }

    // Selects the acquire-stage source by pattern-matching the configured location (design §A.2). Returns a
    // disposable so an owned HttpClient is released; FileSource is a trivial no-op dispose.
    private static IDisposableOpenApiSource CreateSource(OpenApiSourceLocation location) => location switch
    {
        OpenApiSourceLocation.File file => new OwnedSource(new FileSource(file.Path), owned: null),
        OpenApiSourceLocation.Https https => CreateHttpsSource(https.Url),
        _ => throw new ArgumentException(
            $"Unsupported OpenAPI source location '{location.GetType().Name}'.", nameof(location)),
    };

    private static IDisposableOpenApiSource CreateHttpsSource(Uri url)
    {
        var httpsSource = new HttpsSource(url);
        return new OwnedSource(httpsSource, owned: httpsSource);
    }

    // Derives the set of per-view DTO component names (each view's by-name RowType/CrudType) in a
    // deterministic ordinal order, matching how the determinism harness assembles the same set.
    private static IReadOnlyCollection<string> CollectDtoComponentNames(IReadOnlyList<ViewModel> views)
    {
        var names = new SortedSet<string>(DeterministicOrder.Comparer);
        foreach (var view in views)
        {
            if (view.RowType is TsNamed rowType)
            {
                names.Add(rowType.Name);
            }

            if (view.CrudType is TsNamed crudType)
            {
                names.Add(crudType.Name);
            }
        }

        return names.ToArray();
    }

    /// <summary>A disposable acquire source wrapper so the pipeline can <c>using</c> any source uniformly.</summary>
    private interface IDisposableOpenApiSource : IOpenApiSource, IDisposable
    {
    }

    // Wraps an IOpenApiSource, disposing an optionally-owned IDisposable (the HttpsSource's HttpClient) when
    // the pipeline is done. A FileSource owns nothing, so its owned reference is null.
    private sealed class OwnedSource : IDisposableOpenApiSource
    {
        private readonly IOpenApiSource _source;
        private readonly IDisposable? _owned;

        public OwnedSource(IOpenApiSource source, IDisposable? owned)
        {
            _source = source;
            _owned = owned;
        }

        public Task<Result<ReadOnlyMemory<byte>, AcquireError>> ReadAsync(CancellationToken ct) =>
            _source.ReadAsync(ct);

        public void Dispose() => _owned?.Dispose();
    }
}
