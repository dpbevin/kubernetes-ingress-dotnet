// Copyright (c) 2021 David Bevin
// 
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

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
