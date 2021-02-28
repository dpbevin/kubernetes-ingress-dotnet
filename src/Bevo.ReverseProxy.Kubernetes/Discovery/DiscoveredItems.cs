
using System.Collections.Generic;
using Microsoft.ReverseProxy.Abstractions;

namespace Bevo.ReverseProxy.Kube
{
    public class DiscoveredItems
    {
        public DiscoveredItems(IReadOnlyList<ProxyRoute> routes, IReadOnlyList<Cluster> clusters)
        {
            this.Routes = routes;
            this.Clusters = clusters;
        }

        public IReadOnlyList<ProxyRoute> Routes { get; private set; }

        public IReadOnlyList<Cluster> Clusters { get; private set; }
    }
}
