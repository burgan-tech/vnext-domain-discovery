using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;
using BBT.Workflow.Filtering;
using BBT.Workflow.Scripting.Functions;

/// <summary>
/// Domain List Mapping - read path for the cached "all active domains" registry lookup.
///
/// Wraps GetInstancesTask (task type 15), which lists the instances of the "domain" lifecycle
/// workflow. The function takes NO parameters, so there is nothing to resolve from the request:
/// the query is fully determined here.
///
/// Caching is NOT done here. It is declared on the function itself (attributes.cache) and served by
/// the runtime's read-through function cache: on a HIT this task never runs, so the instance store
/// is never touched. Because the function has no parameters the cache key is a STATIC string
/// (attributes.cache.key), not a Dynamic Expresso expression - there is nothing to compute per
/// request. The evicting side is Workflows/src/InvalidateDomainListCacheMapping.csx; both sides go
/// through DomainCacheKey.ListKey (Mappings/src/DomainCacheKey.csx).
///
/// pageSize 500 in one call is deliberate and legal on THIS path. GetInstanceListInput declares
/// [Range(1, 100)], but that DataAnnotation is only enforced by ASP.NET model binding on the REST
/// controller. This task targets its OWN domain ("discovery"), so GetInstancesTaskExecutor takes the
/// IsSameDomain -> ExecuteLocalAsync branch and constructs GetInstanceListInput directly - no model
/// binding, no clamp anywhere in InstanceQueryAppService. The only real guard on the way to SQL is
/// UnifiedFilterService.ExecutePageIdsAsync (pageSize >= 1 and != int.MaxValue).
/// SetDomain("discovery") below is therefore load-bearing, not decorative: pointing this task at
/// another domain would route it through the REST endpoint, where [Range(1, 100)] DOES apply and a
/// pageSize of 500 answers 400. (Precedent for >100 on the local path:
/// north-start-flow/sample/Tasks/ns-bench-instances.json uses pageSize 200.)
/// </summary>
public class DomainListMapping : ScriptBase, IMapping
{
    private const string TargetDomain = "discovery";
    private const string TargetFlow = "domain";

    /// <summary>One page, sized to cover every registered domain. See the class summary.</summary>
    private const int PageSize = 500;

    public Task<ScriptResponse> InputHandler(WorkflowTask task, ScriptContext context)
    {
        var getInstancesTask = task as GetInstancesTask;
        if (getInstancesTask == null)
        {
            throw new InvalidOperationException("Task must be a GetInstancesTask");
        }

        getInstancesTask.SetDomain(TargetDomain);
        getInstancesTask.SetFlow(TargetFlow);
        getInstancesTask.SetPage(1);
        getInstancesTask.SetPageSize(PageSize);

        // "status" is an INSTANCE COLUMN (bare name), not an instance-data attribute - attributes
        // would need an "attributes." prefix. "A" is Active: the list surface accepts the name or
        // the code, and the code is what the column stores.
        //
        // The "domain" workflow has a single, non-terminal state today, so every registered domain
        // is Active and this clause narrows nothing yet. It is here so that a domain instance that
        // is ever cancelled or completed drops out of the list on its own, with no code change.
        //
        // Built through InstanceQuery rather than a hand-written JSON string on purpose:
        // GetInstancesTaskExecutor runs InstanceQueryValidator over the filter BEFORE querying and
        // fails the task on an unknown column, so a typo here surfaces as a loud task failure
        // instead of a silently unfiltered read.
        getInstancesTask.SetFilterSpec(
            InstanceQuery.Create()
                .Where("status", f => f.Eq("A"))
                .OrderBy("key")
                .Build());

        LogInformation($"domain-list -> {TargetDomain}/{TargetFlow} page=1 pageSize={PageSize} status=A");

        return Task.FromResult(new ScriptResponse());
    }

    public Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        // Deliberately NOT wrapped in a catch that returns an empty list. This function's response
        // - status code included - is what the function cache stores, so a swallowed failure would
        // be served as an authoritative "no domains are registered" for the whole 86400s TTL, and
        // consumers of a service-discovery registry act on that answer. Letting the exception out
        // keeps the failure uncacheable: FunctionAppService gates the cache write behind
        // executeResult.IsSuccess AND responseResult.IsSuccess, and returns before writing on
        // either failure. A loud 500 that self-heals on the next request beats a silent empty
        // registry that persists for a day.
        var rows = ResolveRows(context);
        var items = CreateList();
        var skipped = 0;

        foreach (var row in rows)
        {
            if (row == null)
            {
                continue;
            }

            // The runtime type is GetInstanceOutput (src/BBT.Workflow.Domain/Instances/DTOs/),
            // whose instance payload is "attributes" - that is also what the production
            // north-start-flow GetInstances mapping reads. The published docs page for this task
            // (vnext-docs docs/components/tasks/get-instances.md) instead shows items[].data, an
            // older envelope. Rather than bet on which one the deployed runtime emits, accept both:
            // whichever member is present is the instance payload, and the four field names below
            // are identical under either shape.
            var attributes = Child(row, "attributes") ?? Child(row, "Attributes")
                          ?? Child(row, "data") ?? Child(row, "Data");

            // The instance key IS the domain name - RegisterDomainLifecycleMapping.csx calls
            // startTask.SetKey(domainName) - so it is the more reliable of the two sources.
            var domainName = Text(row, "key");
            if (domainName.Length == 0)
            {
                domainName = Text(attributes, "domainName");
            }

            var baseUrl = Text(attributes, "baseUrl");

            if (domainName.Length == 0 || baseUrl.Length == 0)
            {
                // A registered domain without a baseUrl cannot be routed to. Dropping it keeps
                // the contract honest: every item in the list is callable.
                skipped++;
                continue;
            }

            ListAdd(items, new Dictionary<string, object>
            {
                ["domainName"] = domainName,
                ["baseUrl"] = baseUrl,
                ["appId"] = Text(attributes, "appId"),
                ["healthUrl"] = Text(attributes, "healthUrl")
            });
        }

        if (skipped > 0)
        {
            LogWarning($"domain-list: {skipped} domain instance(s) omitted (missing key or baseUrl)");
        }

        // HasNext is computed by the executor as ItemCount == PageSize. If it is true we are
        // sitting exactly on the 500 ceiling and the list is probably incomplete - say so
        // loudly, because the response itself looks perfectly normal and would be cached as-is.
        if (HasNextPage(context))
        {
            LogWarning(
                $"domain-list: the page is full at pageSize={PageSize} (HasNext=true); " +
                "the list may be truncated - add a second page task to the function");
        }

        return Task.FromResult(new ScriptResponse
        {
            Key = "domain-list-loaded",
            Data = new Dictionary<string, object>
            {
                ["items"] = items
            },
            Tags = new[] { "domain", "discovery", "list", "success" }
        });
    }

    /// <summary>
    /// Finds the instance rows in the task response. The GetInstances payload is
    /// InstanceListWithGroupsResponse: { "links": ..., "items": [ ... ] }. context.Body carries the
    /// task's StandardTaskResponse on the single-task path; context.TaskResponse is probed as a
    /// fallback so the mapping keeps working if the function is ever recomposed as multi-task.
    ///
    /// Throws when the list cannot be located at all. An EMPTY "items" array and a payload whose
    /// shape this mapping does not recognize are NOT the same answer: the first means "no domains
    /// are registered" and is cacheable; the second means the task or the runtime contract changed
    /// under us, and serving it as an empty registry for a full TTL would be a silent outage. That
    /// is why the probe reports whether it FOUND a list, rather than just returning an empty one.
    /// </summary>
    private List<object?> ResolveRows(ScriptContext context)
    {
        List<object?> rows;

        if (TryUnwrap((object?)context.Body, out rows))
        {
            return rows;
        }

        if (context.TaskResponse != null)
        {
            foreach (var pair in context.TaskResponse)
            {
                if (TryUnwrap((object?)pair.Value, out rows))
                {
                    return rows;
                }
            }
        }

        throw new InvalidOperationException(
            "get-domain-instances did not return a recognizable instance list; expected an 'items' " +
            "array on the task response (InstanceListWithGroupsResponse). Refusing to report an " +
            "empty domain registry, which would be cached as authoritative.");
    }

    /// <summary>
    /// Locates the "items" array on a task response, unwrapping the StandardTaskResponse envelope.
    /// Returns true only when the array was actually found - an empty array still counts as found.
    /// </summary>
    private bool TryUnwrap(object? response, out List<object?> rows)
    {
        rows = CreateList();

        if (response == null)
        {
            return false;
        }

        object? payload = response;
        if (HasProperty(payload, "data"))
        {
            payload = GetPropertyValue(payload, "data");
        }
        else if (HasProperty(payload, "Data"))
        {
            payload = GetPropertyValue(payload, "Data");
        }

        if (payload == null)
        {
            return false;
        }

        if (HasProperty(payload, "items"))
        {
            rows = GetList(payload, "items");
            return true;
        }

        if (HasProperty(payload, "Items"))
        {
            rows = GetList(payload, "Items");
            return true;
        }

        return false;
    }

    /// <summary>Reads the executor's HasNext metadata flag; false when it cannot be found.</summary>
    private bool HasNextPage(ScriptContext context)
    {
        var body = (object?)context.Body;
        if (body == null)
        {
            return false;
        }

        var metadata = Child(body, "metadata") ?? Child(body, "Metadata");
        if (metadata == null)
        {
            return false;
        }

        var raw = Text(metadata, "HasNext");
        if (raw.Length == 0)
        {
            raw = Text(metadata, "hasNext");
        }

        bool parsed;
        return bool.TryParse(raw, out parsed) && parsed;
    }

    private object? Child(object? parent, string name)
    {
        if (parent == null || !HasProperty(parent, name))
        {
            return null;
        }

        return GetPropertyValue(parent, name);
    }

    private string Text(object? source, string name)
    {
        if (source == null || !HasProperty(source, name))
        {
            return string.Empty;
        }

        var value = GetPropertyValue(source, name);
        return value == null ? string.Empty : value.ToString();
    }
}
