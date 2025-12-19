using System.Threading.Tasks;
using BBT.Workflow.Scripting;

/// <summary>
/// Skip Health Check Rule - Checks if health check should be skipped
/// Returns true if healthUrl is missing OR (existing domain AND healthUrl didn't change)
/// </summary>
public class SkipHealthCheckRule : IConditionMapping
{
    public async Task<bool> Handler(ScriptContext context)
    {
        try
        {
            if (context?.Instance?.Data == null)
                return true;  // If no data, skip

            // Check if healthUrl was retrieved
            var healthUrlRetrieved = context.Instance.Data.healthUrlRetrieved;
            if (healthUrlRetrieved != true)
                return true;  // No healthUrl, skip

            // Check if it's an existing domain
            var domainExists = context.Instance.Data.domainExists;
            if (domainExists == true)
            {
                // For existing domain, check if healthUrl changed
                var existingDomain = context.Instance.Data.existingDomain;
                var domainData = context.Instance.Data.domainData;
                
                if (existingDomain != null && domainData != null)
                {
                    var existingHealthUrl = existingDomain._healthUrl?.ToString();
                    var newHealthUrl = domainData._healthUrl?.ToString();
                    
                    // If healthUrl didn't change, skip
                    if (existingHealthUrl == newHealthUrl)
                        return true;
                }
            }

            // HealthUrl exists and (new domain OR changed), don't skip
            return false;
        }
        catch (Exception)
        {
            return true;  // On error, skip
        }
    }
}

