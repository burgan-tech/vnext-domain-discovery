using System.Threading.Tasks;
using BBT.Workflow.Scripting;

/// <summary>
/// Domain Exists Rule - Checks if domain already exists in Redis
/// </summary>
public class DomainExistsRule : IConditionMapping
{
    public async Task<bool> Handler(ScriptContext context)
    {
        try
        {
            if (context?.Instance?.Data == null)
                return false;

            var domainExists = context.Instance.Data.domainExists;
            
            if (domainExists == null)
                return false;

            return domainExists == true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}



