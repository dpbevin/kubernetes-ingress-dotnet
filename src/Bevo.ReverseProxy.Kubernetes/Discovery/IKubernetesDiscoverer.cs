using System.Threading;
using System.Threading.Tasks;

namespace Bevo.ReverseProxy.Kube
{
    public interface IKubernetesDiscoverer
    {
        /// <summary>
        /// Execute the discovery and update entities.
        /// </summary>
        Task<DiscoveredItems> DiscoverAsync(CancellationToken cancellation);
    }
}
