// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Collections.Generic;
using Microsoft.ReverseProxy.Abstractions;

namespace Bevo.ReverseProxy.Kube
{
    public class BackendConfiguration
    {
        public string ConfigurationHash { get; internal set; }

        public IReadOnlyList<ProxyRoute> Routes { get; internal set; }

        public IReadOnlyList<Cluster> Clusters { get; internal set; }
    }
}
