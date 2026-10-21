using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace MickMouse.Web.Infrastructure;

public static class ParameterStoreSettings
{
    public static async Task<string> LoadConnectionStringAsync(IAmazonSimpleSystemsManagement client,
        string parameterName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            throw new InvalidOperationException("Configure AWS:ConnectionParameter before starting AWS mode.");
        var response = await client.GetParameterAsync(new GetParameterRequest
        {
            Name = parameterName,
            WithDecryption = true
        }, ct);
        if (response.Parameter?.Type != ParameterType.SecureString || string.IsNullOrWhiteSpace(response.Parameter.Value))
            throw new InvalidOperationException("The database parameter must be a non-empty SecureString.");
        return response.Parameter.Value;
    }
}
