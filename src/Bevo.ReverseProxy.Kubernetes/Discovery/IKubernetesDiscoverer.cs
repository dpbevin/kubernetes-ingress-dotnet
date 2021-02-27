using Microsoft.ReverseProxy.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bevo.ReverseProxy.Kube.Discovery
{
    public interface IKubernetesDiscoverer
    {
        /// <summary>
        /// Execute the discovery and update entities.
        /// </summary>
        Task<(IReadOnlyList<ProxyRoute> Routes, IReadOnlyList<Cluster> Clusters)> DiscoverAsync(CancellationToken cancellation);
    }
}
