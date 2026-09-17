using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BBT.Workflow.Scripting;
using BBT.Workflow.Definitions;
using BBT.Workflow.Scripting.Functions;

/// <summary>
/// Domain List Output - shapes the function response and owns its HTTP status code.
///
/// Same reason for existing as DomainLookupOutput: the legacy single-task extraction path always
/// answers 200 and ScriptResponse.StatusCode is only honoured from a function-level output handler
/// (attributes.output), so the envelope is built here.
///
/// Response contract (the function declares rawResponse: true, so Data is returned verbatim):
///     200 -> { "items": [ { "domainName", "baseUrl", "appId", "healthUrl" } ] }
///
/// An EMPTY registry is 200 with an empty array, never 404. Two reasons: "no domains registered" is
/// a valid answer to "list the domains", not a missing resource; and the function cache stores the
/// whole FunctionResponseOutput including the status code, so a 404 would replay as a 404 for the
/// full TTL - the exact failure domain-lookup has to evict around.
/// </summary>
public class DomainListOutput : ScriptBase, IOutputHandler
{
    /// <summary>ToVariableName("get-domain-instances") - the OutputResponse slot of the task.</summary>
    private const string TaskVariable = "getDomainInstances";

    public Task<ScriptResponse> OutputHandler(ScriptContext context)
    {
        object? result = null;
        if (context.OutputResponse != null && context.OutputResponse.TryGetValue(TaskVariable, out var stored))
        {
            result = stored;
        }

        var items = ResolveItems(result);

        return Task.FromResult(new ScriptResponse
        {
            Key = "domain-list-found",
            StatusCode = 200,
            Data = new Dictionary<string, object>
            {
                ["items"] = items
            },
            Tags = new[] { "domain", "discovery", "list", "success" }
        });
    }

    /// <summary>
    /// Pulls the projected rows out of the task mapping's response. DomainListMapping already
    /// reduced every instance to the four contract fields, so nothing is re-shaped here - this only
    /// unwraps, and falls back to an empty list rather than letting a null reach the caller.
    /// </summary>
    private List<object?> ResolveItems(object? result)
    {
        if (result == null)
        {
            return CreateList();
        }

        if (HasProperty(result, "items"))
        {
            return GetList(result, "items");
        }

        if (HasProperty(result, "Items"))
        {
            return GetList(result, "Items");
        }

        return CreateList();
    }
}
