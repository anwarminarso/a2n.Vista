// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using a2n.Vista.Client.TypeScript.Emit;
using a2n.Vista.Client.TypeScript.Modeling;
using a2n.Vista.Client.TypeScript.Parse;
using a2n.Vista.Client.TypeScript.Pipeline;
using a2n.Vista.Client.TypeScript.Resolve;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Unit tests for the per-view read-client emitter (task 10.6; Requirements 4.1–4.7, 7.2–7.4, 9.1/9.2). They
/// assert, against the canonical fixture's <c>Customers</c> view, that exactly the four read facets are
/// emitted (and no write facet), that each method sends the document's method + path, that the success types
/// track the document (including the raw export payload), that a secured facet short-circuits with a typed
/// unauthorized result before sending, and that the output is deterministic.
/// </summary>
public sealed class ViewClientEmitterTests
{
    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    // Runs parse -> resolve -> re-lift -> operation-graph over the fixture and returns the one Customers view.
    private static ViewModel BuildCustomersView()
    {
        var raw = File.ReadAllText(Path.Combine(FixturesDirectory, "valid-vista-document.json"));

        var parsed = OpenApiParser.Parse(raw);
        if (parsed.IsError)
        {
            throw new Exception($"Fixture failed to parse: {parsed.Error.Message}");
        }

        var resolved = RefResolver.Resolve(parsed.Value);
        if (resolved.IsError)
        {
            throw new Exception($"Fixture failed to resolve: {resolved.Error.Message}");
        }

        var notices = new NoticeCollector();
        var reLift = new EnvelopeReLifter(new EnvelopeCatalog()).ReLift(resolved.Value, notices);
        var views = new OperationGraphBuilder().Build(resolved.Value, reLift, notices);

        return views.Single(view => string.Equals(view.ViewName, "Customers", StringComparison.Ordinal));
    }

    // A hand-built view with secured read facets, so the secured code path (auth imports + credential block)
    // is exercised independently of the upstream security-posture modeling.
    private static ViewModel BuildSecuredOrdersView()
    {
        var list = new FacetModel(
            OperationGraphBuilder.ListSuffix,
            "POST",
            "/api/views/orders/list",
            RequestType: TsType.Named("VistaListRequestBody"),
            SuccessType: TsType.Generic("ViewListResult", new[] { TsType.Named("OrderRow") }),
            Secured: true,
            Concurrency: ConcurrencyMode.None);
        var metadata = new FacetModel(
            OperationGraphBuilder.MetadataSuffix,
            "GET",
            "/api/views/orders/metadata",
            RequestType: null,
            SuccessType: TsType.Named("VistaMetadataResponse"),
            Secured: true,
            Concurrency: ConcurrencyMode.None);

        return new ViewModel("Orders", "/api/views/orders", TsType.Named("OrderRow"), null, new[] { list, metadata });
    }

    // A hand-built writable view (task 10.7): a read `list`, an anonymous `create`, and token-bearing
    // `update`/`delete` (documented 428/409 → ConcurrencyMode.TokenBearing), so the gated write surface and
    // its concurrency handling are exercised independently of the upstream operation-graph modeling.
    private static ViewModel BuildWritableOrdersView()
    {
        var list = new FacetModel(
            OperationGraphBuilder.ListSuffix,
            "POST",
            "/api/views/orders/list",
            RequestType: TsType.Named("VistaListRequestBody"),
            SuccessType: TsType.Generic("ViewListResult", new[] { TsType.Named("OrderRow") }),
            Secured: false,
            Concurrency: ConcurrencyMode.None);
        var create = new FacetModel(
            OperationGraphBuilder.CreateSuffix,
            "POST",
            "/api/views/orders/create",
            RequestType: TsType.Named("VistaWriteRequestBody"),
            SuccessType: TsType.Named("VistaWriteResponse"),
            Secured: false,
            Concurrency: ConcurrencyMode.None);
        var update = new FacetModel(
            OperationGraphBuilder.UpdateSuffix,
            "POST",
            "/api/views/orders/update",
            RequestType: TsType.Named("VistaWriteRequestBody"),
            SuccessType: TsType.Named("VistaWriteResponse"),
            Secured: false,
            Concurrency: ConcurrencyMode.TokenBearing);
        var delete = new FacetModel(
            OperationGraphBuilder.DeleteSuffix,
            "POST",
            "/api/views/orders/delete",
            RequestType: TsType.Named("VistaWriteRequestBody"),
            SuccessType: TsType.Named("VistaWriteResponse"),
            Secured: false,
            Concurrency: ConcurrencyMode.TokenBearing);

        // Facets are stored pre-sorted by suffix (ordinal): create, delete, list, update.
        return new ViewModel(
            "Orders",
            "/api/views/orders",
            TsType.Named("OrderRow"),
            TsType.Named("OrderCrud"),
            new[] { create, delete, list, update });
    }

    [Test]
    public async Task Emits_No_Write_Facet_When_Write_Generation_Is_Disabled()
    {
        // Default (backward-compatible) overload and the explicit disabled overload both omit every write
        // operation (Requirement 5.1).
        var defaultContent = ViewClientEmitter.Emit(BuildWritableOrdersView()).Content;
        var disabledContent = ViewClientEmitter.Emit(BuildWritableOrdersView(), emitWriteFacets: false).Content;

        foreach (var content in new[] { defaultContent, disabledContent })
        {
            await Assert.That(content).DoesNotContain("create(");
            await Assert.That(content).DoesNotContain("update(");
            await Assert.That(content).DoesNotContain("delete(");
            await Assert.That(content).DoesNotContain("VistaWriteResponse");
            // The read facet is still present.
            await Assert.That(content).Contains("list(body: VistaListRequestBody):");
        }

        // The two disabled forms are byte-identical (the default delegates to emitWriteFacets: false).
        await Assert.That(disabledContent).IsEqualTo(defaultContent);
    }

    [Test]
    public async Task Emits_No_Write_Facet_For_A_Read_Only_View_When_Enabled()
    {
        // Even with write generation enabled, a read-only view (no create/update/delete in its operation
        // set) emits no write operation (Requirement 5.3).
        var content = ViewClientEmitter.Emit(BuildCustomersView(), emitWriteFacets: true).Content;

        await Assert.That(content).DoesNotContain("create(");
        await Assert.That(content).DoesNotContain("update(");
        await Assert.That(content).DoesNotContain("delete(");
    }

    [Test]
    public async Task Emits_The_Typed_Write_Facets_When_Enabled_And_Writable()
    {
        var content = ViewClientEmitter.Emit(BuildWritableOrdersView(), emitWriteFacets: true).Content;

        // create takes the typed TCrud model and returns VistaWriteResponse (Requirement 5.4).
        await Assert.That(content).Contains("create(model: OrderCrud): Promise<ClientResult<VistaWriteResponse>> {");
        // update takes the typed TCrud model; delete takes no model (Requirement 5.5).
        await Assert.That(content).Contains("update(model: OrderCrud, options?: { readonly ifMatch?: string }): Promise<ClientResult<VistaWriteResponse>> {");
        await Assert.That(content).Contains("delete(options?: { readonly ifMatch?: string }): Promise<ClientResult<VistaWriteResponse>> {");

        // create sends the model as the JSON body (Requirement 5.4).
        await Assert.That(content).Contains("JSON.stringify(model)");
        // The write success/crud types are imported from ../types.
        await Assert.That(content).Contains("VistaWriteResponse");
        await Assert.That(content).Contains("OrderCrud");
    }

    [Test]
    public async Task Token_Bearing_Writes_Accept_If_Match_And_Set_The_Header()
    {
        var content = ViewClientEmitter.Emit(BuildWritableOrdersView(), emitWriteFacets: true).Content;

        // The send helper gains a trailing If-Match value and sets the header from it (Requirement 5.6).
        await Assert.That(content).Contains("ifMatch?: string,");
        await Assert.That(content).Contains("if (ifMatch !== undefined) {");
        await Assert.That(content).Contains("headers[\"If-Match\"] = ifMatch;");
        // The token-bearing update/delete thread the caller-supplied token through.
        await Assert.That(content).Contains("options?.ifMatch,");
        // The distinct 428/409 outcomes are produced by the shared classifier (documented on the method).
        await Assert.That(content).Contains("`precondition-required` (428)");
    }

    [Test]
    public async Task Write_Facets_Emit_In_Ordinal_Suffix_Order_After_The_Read_Facets()
    {
        var content = ViewClientEmitter.Emit(BuildWritableOrdersView(), emitWriteFacets: true).Content;

        var listIndex = content.IndexOf("list(body:", StringComparison.Ordinal);
        var createIndex = content.IndexOf("create(model:", StringComparison.Ordinal);
        var deleteIndex = content.IndexOf("delete(options?:", StringComparison.Ordinal);
        var updateIndex = content.IndexOf("update(model:", StringComparison.Ordinal);

        // Read facet first, then write facets in ordinal suffix order (create < delete < update).
        await Assert.That(listIndex).IsGreaterThan(0);
        await Assert.That(createIndex).IsGreaterThan(listIndex);
        await Assert.That(deleteIndex).IsGreaterThan(createIndex);
        await Assert.That(updateIndex).IsGreaterThan(deleteIndex);
    }

    [Test]
    public async Task Write_Facet_Output_Is_Deterministic()
    {
        var first = ViewClientEmitter.Emit(BuildWritableOrdersView(), emitWriteFacets: true).Content;
        var second = ViewClientEmitter.Emit(BuildWritableOrdersView(), emitWriteFacets: true).Content;

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(first).EndsWith("\n");
        await Assert.That(first).DoesNotContain("\r");
    }

    [Test]
    public async Task Emits_The_Kebab_Case_View_File_Path()
    {
        var file = ViewClientEmitter.Emit(BuildCustomersView());
        await Assert.That(file.RelativePath).IsEqualTo("views/customers.ts");
    }

    [Test]
    public async Task Derives_Kebab_Case_File_Names()
    {
        await Assert.That(ViewClientEmitter.FileName("Customers")).IsEqualTo("customers");
        await Assert.That(ViewClientEmitter.FileName("OrderDetails")).IsEqualTo("order-details");
    }

    [Test]
    public async Task Emits_A_Client_Class_Named_After_The_View()
    {
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;
        await Assert.That(content).Contains("export class CustomersClient {");
        await Assert.That(content).Contains("constructor(private readonly ctx: ClientContext) {}");
    }

    [Test]
    public async Task Emits_Exactly_The_Present_Read_Facets_And_No_Write_Facet()
    {
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;

        // All four read facets are present (Requirement 4.1).
        await Assert.That(content).Contains("list(body: VistaListRequestBody): Promise<ClientResult<ViewListResult<CustomerRow>>> {");
        await Assert.That(content).Contains("detail(body: VistaDetailRequestBody): Promise<ClientResult<CustomerRow>> {");
        await Assert.That(content).Contains("metadata(): Promise<ClientResult<VistaMetadataResponse>> {");
        await Assert.That(content).Contains("export(body: VistaListRequestBody): Promise<ClientResult<RawPayload>> {");

        // No write facet is emitted by this task (10.7 owns them).
        await Assert.That(content).DoesNotContain("create(");
        await Assert.That(content).DoesNotContain("update(");
        await Assert.That(content).DoesNotContain("delete(");
    }

    [Test]
    public async Task Sends_The_Documented_Method_And_Path_Per_Facet()
    {
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;

        // list/detail/export are POST; metadata is GET; each carries its full document path (Requirement 4.2).
        await Assert.That(content).Contains("\"POST\",\n      \"/api/views/customers/list\",");
        await Assert.That(content).Contains("\"POST\",\n      \"/api/views/customers/detail\",");
        await Assert.That(content).Contains("\"GET\",\n      \"/api/views/customers/metadata\",");
        await Assert.That(content).Contains("\"POST\",\n      \"/api/views/customers/export\",");
    }

    [Test]
    public async Task Metadata_Takes_No_Argument_And_Sends_No_Body()
    {
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;

        // metadata has no request argument (Requirement 4.6): its send call passes `undefined` for the body.
        // The fixture's facets are anonymous, so the client is auth-free (no `secured` argument).
        await Assert.That(content).Contains("metadata(): Promise<ClientResult<VistaMetadataResponse>> {");
        await Assert.That(content).Contains("\"metadata\",\n      \"GET\",\n      \"/api/views/customers/metadata\",\n      undefined,");
    }

    [Test]
    public async Task Export_Preserves_The_Raw_Unparsed_Payload()
    {
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;

        // The export success payload is RawPayload, parsed by returning the body verbatim (Requirement 4.7).
        await Assert.That(content).Contains("Promise<ClientResult<RawPayload>>");
        await Assert.That(content).Contains("(raw) => raw,");
        await Assert.That(content).Contains("import type { RawPayload } from \"../runtime/raw-payload\";");
    }

    [Test]
    public async Task Emits_The_Credential_Block_Before_The_Send_For_A_Secured_Client()
    {
        var content = ViewClientEmitter.Emit(BuildSecuredOrdersView()).Content;

        // A secured client obtains a credential before sending and short-circuits when none is available
        // (Requirements 7.2–7.4).
        await Assert.That(content).Contains("if (secured) {");
        await Assert.That(content).Contains("credential = await this.ctx.getCredential(operation);");
        await Assert.That(content).Contains("if (credential === null) {");
        await Assert.That(content).Contains("return unauthorized<T>(");
        await Assert.That(content).Contains("headers[credential.headerName] = credential.headerValue;");

        // The unauthorized short-circuit sits before the transport send (no request on failure, 7.3/7.4).
        var unauthorizedIndex = content.IndexOf("return unauthorized<T>(", StringComparison.Ordinal);
        var sendIndex = content.IndexOf("await this.ctx.transport.send(request);", StringComparison.Ordinal);
        await Assert.That(unauthorizedIndex).IsGreaterThan(0);
        await Assert.That(sendIndex).IsGreaterThan(unauthorizedIndex);
    }

    [Test]
    public async Task Emits_Secured_True_And_The_Auth_Imports_For_A_Secured_Facet()
    {
        var content = ViewClientEmitter.Emit(BuildSecuredOrdersView()).Content;

        // The secured flag flows into the send call as the boolean literal `true`.
        await Assert.That(content).Contains("\"/api/views/orders/metadata\",\n      true,\n      undefined,");
        // The auth contracts are imported only because a secured facet is present (Requirement 7.2).
        await Assert.That(content).Contains("import type { AuthCredential, OperationInfo } from \"../runtime/auth\";");
        await Assert.That(content).Contains("import { classifyResponse, transportError, unauthorized } from \"../runtime/result\";");
        await Assert.That(content).Contains("const operation: OperationInfo = { view: \"Orders\", facet, secured: true };");
    }

    [Test]
    public async Task Omits_All_Auth_When_No_Facet_Is_Secured()
    {
        // The fixture's facets are modeled as anonymous (their security is a document-level default not yet
        // folded into FacetModel.Secured), so the client is entirely auth-free: no auth import, no
        // `unauthorized` value import, no `secured` parameter, and no credential block (Requirement 7.5).
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;

        await Assert.That(content).Contains("import { classifyResponse, transportError } from \"../runtime/result\";");
        await Assert.That(content).DoesNotContain("from \"../runtime/auth\";");
        await Assert.That(content).DoesNotContain("if (secured) {");
        await Assert.That(content).DoesNotContain("unauthorized");
    }

    [Test]
    public async Task Routes_Through_The_Transport_And_Classifies_Without_Throwing()
    {
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;

        await Assert.That(content).Contains("response = await this.ctx.transport.send(request);");
        await Assert.That(content).Contains("return transportError<T>(error);");
        await Assert.That(content).Contains("return classifyResponse(classifiable, parseSuccess);");
        await Assert.That(content).Contains("contentType: readContentType(response.headers),");
    }

    [Test]
    public async Task Sets_Json_Content_Type_Only_When_A_Body_Is_Present()
    {
        var content = ViewClientEmitter.Emit(BuildCustomersView()).Content;
        await Assert.That(content).Contains("if (body !== undefined) {");
        await Assert.That(content).Contains("headers[\"Content-Type\"] = \"application/json\";");
    }

    [Test]
    public async Task Is_Byte_For_Byte_Deterministic_With_Lf_And_A_Single_Trailing_Newline()
    {
        var first = ViewClientEmitter.Emit(BuildCustomersView()).Content;
        var second = ViewClientEmitter.Emit(BuildCustomersView()).Content;

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(first).EndsWith("\n");
        await Assert.That(first).DoesNotContain("\r");
        await Assert.That(first).StartsWith("// <auto-generated>");
    }

    [Test]
    public async Task EmitAll_Emits_One_File_Per_View()
    {
        var view = BuildCustomersView();
        var files = ViewClientEmitter.EmitAll(new[] { view });

        await Assert.That(files.Count).IsEqualTo(1);
        await Assert.That(files[0].RelativePath).IsEqualTo("views/customers.ts");
    }
}
