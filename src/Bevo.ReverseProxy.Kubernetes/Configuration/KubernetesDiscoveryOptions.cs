
using System;

namespace Bevo.ReverseProxy.Kube
{
    public sealed class KubernetesDiscoveryOptions
    {
        public TimeSpan DiscoveryPeriod { get; set; } = TimeSpan.FromSeconds(30);
    }
}
