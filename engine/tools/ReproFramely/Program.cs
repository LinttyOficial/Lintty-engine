using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Lintty.Engine.Reporter;
using QuestPDF;

QuestPDF.Settings.EnableDebugging = true;

// Repro harness for the Framely "conflicting size constraints" bug.
// Synthesizes a JSON that mimics the production-scale Framely report:
//   - long FQNs (Framely.Infrastructure.Services.X.Y(Guid, CancellationToken))
//   - long file paths (backend/Framely.Infrastructure/Services/Foo.cs)
//   - LNTY-008 (missing port) + LNTY-009 (long methods) + 1 hard lock LNTY-001
//
// Run:
//   dotnet run --project engine/tools/ReproFramely -- engine/laudo-framely.json engine/laudo-framely.pdf
// or, with no args, writes ./laudo-framely.json (synthetic) and ./laudo-framely.pdf
// next to the cwd.

string jsonPath;
string pdfPath;

if (args.Length >= 2)
{
    jsonPath = args[0];
    pdfPath = args[1];
}
else
{
    var cwd = Directory.GetCurrentDirectory();
    jsonPath = Path.Combine(cwd, "laudo-framely.json");
    pdfPath = Path.Combine(cwd, "laudo-framely.pdf");
}

string json;
if (File.Exists(jsonPath))
{
    Console.Error.WriteLine($"[repro] Loading existing JSON: {jsonPath}");
    json = File.ReadAllText(jsonPath);
}
else
{
    Console.Error.WriteLine($"[repro] Synthesizing Framely-like JSON at: {jsonPath}");
    json = SynthesizeFramelyJson();
    File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

try
{
    var hash = new PdfReporter().GeneratePdf(json, pdfPath);
    Console.Error.WriteLine($"[repro] OK hash_content={hash}");
    Console.Error.WriteLine($"[repro] PDF written to: {pdfPath}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[repro] FAIL {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    Environment.Exit(1);
}

static string SynthesizeFramelyJson()
{
    // Pathological inputs typical of a real .NET solution:
    //   - deep paths
    //   - long generic method signatures
    //   - long monospace code snippets
    var v = new System.Collections.Generic.List<object>();

    // 1 hard lock LNTY-001 (using directive in Domain)
    v.Add(new
    {
        rule_id = "LNTY-001",
        severity = "critical",
        is_hard_lock = true,
        file = "backend/Framely.Domain/Aggregates/ParentalConsent/ParentalConsentAggregate.cs",
        line = 6,
        column = 1,
        symbol_fqn = "Microsoft.EntityFrameworkCore",
        evidence = new
        {
            code_snippet = "using Microsoft.EntityFrameworkCore;",
            ast_kind = "UsingDirectiveSyntax",
            additional_context = new System.Collections.Generic.Dictionary<string, string>
            {
                ["detection_pass"] = "using_directive",
                ["forbidden_namespace"] = "Microsoft.EntityFrameworkCore",
            },
        },
        fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa01",
    });

    // ~20 LNTY-008 (missing port) with long FQNs and paths
    var services = new[]
    {
        ("ParentalConsentService",       "RequestPublicizeProfileAsync"),
        ("DocumentOcrService",           "ExtractBirthDateFromBrazilianRgAsync"),
        ("EmailNotificationDispatcher",  "DispatchTransactionalNotificationToParentAsync"),
        ("BillingReconciliationWorker",  "ReconcileMonthlyBillingForOrganizationAsync"),
        ("WhatsAppOnboardingChannel",    "SendOnboardingConsentLinkToGuardianAsync"),
        ("StripeSubscriptionGateway",    "CreateRecurringSubscriptionForFamilyPlanAsync"),
        ("FirebaseDeviceTokenRegistry",  "RegisterDeviceTokenForChildProfileAsync"),
        ("SendgridTemplatedMailerClient","SendPasswordResetWithSecurityCodeAsync"),
        ("AzureBlobStorageMediaUploader","UploadProfileMediaWithThumbnailGenerationAsync"),
        ("PostgresEventStore",           "AppendDomainEventToAggregateStreamAsync"),
        ("RabbitMqOutboxPublisher",      "PublishPendingIntegrationEventsBatchAsync"),
        ("CloudWatchAuditLogSink",       "EmitStructuredAuditTrailEntryAsync"),
        ("RedisDistributedCacheLayer",   "GetOrSetWithSlidingExpirationAsync"),
        ("S3SignedUrlGenerator",         "GeneratePresignedDownloadUrlForReportAsync"),
        ("PixPaymentBrokerClient",       "InitiatePixInstantTransferToPayoutAccountAsync"),
        ("SendinblueCampaignDispatcher", "ScheduleMarketingCampaignForCohortSegmentAsync"),
        ("OneSignalPushNotifier",        "BroadcastTimeSensitivePushToSubscribedDevicesAsync"),
        ("CnpjValidationServiceClient",  "ValidateCorporateTaxIdAgainstReceitaFederalAsync"),
        ("ViaCepAddressLookupClient",    "ResolvePostalCodeToFullStructuredAddressAsync"),
        ("HereMapsGeocodingClient",      "ReverseGeocodeCoordinatesToHumanReadableLocationAsync"),
    };

    int line = 12;
    int idx = 1;
    foreach (var (svc, _) in services)
    {
        // Deeper paths to mimic real Framely repos (six segments deep, no breaks)
        var deepFile = $"backend/src/main/Framely.Infrastructure.External.Integrations/Services/Adapters/{svc}.cs";
        var longFqn = $"Framely.Infrastructure.External.Integrations.Services.Adapters.{svc}+InternalDispatcher`1[Framely.Domain.Aggregates.ParentalConsent.ParentalConsentAggregateRoot]";
        v.Add(new
        {
            rule_id = "LNTY-008",
            severity = "high",
            is_hard_lock = false,
            file = deepFile,
            line = line++,
            column = 21,
            symbol_fqn = longFqn,
            evidence = new
            {
                code_snippet = $"public class {svc} : I{svc}",
                ast_kind = "ClassDeclarationSyntax",
                additional_context = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["expected_in"] = "domain",
                    ["missing_port"] = $"I{svc}",
                },
            },
            fingerprint = $"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb{idx:D4}",
        });
        idx++;
    }

    // ~25 LNTY-009 (long methods) with very long FQNs and snippets
    foreach (var (svc, method) in services)
    {
        var fqn = $"Framely.Infrastructure.External.Integrations.Services.Adapters.{svc}.{method}(System.Guid, System.Threading.CancellationToken, Framely.Application.Contracts.RequestMetadataDescriptor)";
        // Code snippet with ONE physical line that exceeds page width (no line breaks, single very long string).
        var snippet = $"public async Task<Framely.Domain.Aggregates.{svc}.Operations.{method}Result> {method}(System.Guid aggregateId, System.Threading.CancellationToken cancellationToken, Framely.Application.Contracts.RequestMetadataDescriptor metadata)";
        v.Add(new
        {
            rule_id = "LNTY-009",
            severity = "medium",
            is_hard_lock = false,
            file = $"backend/Framely.Infrastructure/Services/{svc}.cs",
            line = line++,
            column = 19,
            symbol_fqn = fqn,
            evidence = new
            {
                code_snippet = snippet,
                ast_kind = "MethodDeclarationSyntax",
                additional_context = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["loc_count"] = "122",
                    ["token_count_estimate"] = "3100",
                    ["tokenizer"] = "placeholder_sprint0",
                },
            },
            fingerprint = $"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc{idx:D4}",
        });
        idx++;
    }

    // 5 LNTY-006 ubiquitous language
    foreach (var nm in new[] { "OrderHelper", "BillingManager", "CustomerUtility", "PaymentHelper", "CommonService" })
    {
        v.Add(new
        {
            rule_id = "LNTY-006",
            severity = "low",
            is_hard_lock = false,
            file = $"backend/Framely.Domain/Helpers/{nm}.cs",
            line = line++,
            column = 18,
            symbol_fqn = $"Framely.Domain.Helpers.{nm}",
            evidence = new
            {
                code_snippet = $"public class {nm}",
                ast_kind = "ClassDeclarationSyntax",
                additional_context = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["detection_kind"] = "ubiquitous_language_leak",
                    ["matched_name"] = nm,
                },
            },
            fingerprint = $"sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd{idx:D4}",
        });
        idx++;
    }

    var report = new
    {
        schema_version = "1.0",
        run_id = "run_framely_synth_0001",
        canon_version = "1.0.0",
        rule_set_version = "1.0.0",
        solution_path = "Framely.sln",
        score = 0,
        grade = "F",
        seal_eligible = false,
        hard_locks_hit = new[] { "LNTY-001" },
        layer_summary = new
        {
            Application    = new { projects = 3, files = 47, violations = 0  },
            Domain         = new { projects = 2, files = 38, violations = 6  },
            Infrastructure = new { projects = 5, files = 72, violations = 45 },
            Presentation   = new { projects = 2, files = 18, violations = 0  },
        },
        violations = v,
        exceptions = Array.Empty<object>(),
        workspace_diagnostics = Array.Empty<object>(),
        inference_signature = (string?)null,
        compile_status = "success",
        metrics = new
        {
            total_sloc_physical = 18742,
            sloc_per_layer = new
            {
                application = 3210,
                domain = 4890,
                infrastructure = 9120,
                presentation = 1522,
            },
            projects_analyzed = 12,
        },
        ai_candidates = Array.Empty<object>(),
        sandbox_integrity = new
        {
            source_destroyed_at = (string?)null,
            egress_violations = Array.Empty<object>(),
        },
        scan_id = "scan_framely_synth_0001",
        audit_chain = (object?)null,
    };

    return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = false });
}
