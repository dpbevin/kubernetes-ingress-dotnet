// Copyright (c) 2021 David Bevin
// 
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;

namespace Bevo.ReverseProxy.Kube
{
    public sealed class KubernetesDiscoveryOptions
    {
        public TimeSpan DiscoveryPeriod { get; set; } = TimeSpan.FromSeconds(30);
    }
}
