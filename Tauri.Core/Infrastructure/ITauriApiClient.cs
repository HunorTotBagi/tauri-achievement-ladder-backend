namespace Tauri.Core.Infrastructure;

/// <summary>
/// Abstraction over the Tauri API transport so services depend on the fetch operation
/// rather than the concrete HTTP client. The composition root owns the concrete
/// client's lifetime and disposal.
/// </summary>
public interface ITauriApiClient
{
    Task<TauriApiResponseResult> FetchResponseElementAsync(
        string endpoint,
        object parameters,
        string requestLabel,
        CancellationToken cancellationToken
    );
}
